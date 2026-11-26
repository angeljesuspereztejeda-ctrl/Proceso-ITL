using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using OpinionesETL.Worker.Application.Configuration;
using OpinionesETL.Worker.Domain.Entities;
using OpinionesETL.Worker.Domain.Interfaces;

namespace OpinionesETL.Worker.Infrastructure.Loaders;

public class DimensionLoader : IDimensionLoader
{
    private readonly EtlSettings _settings;
    private readonly ILoggerService _logger;

    public DimensionLoader(IOptions<EtlSettings> settings, ILoggerService logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<DimensionLoadResult> LoadDimensionsAsync(CancellationToken cancellationToken)
    {
        var resultado = new DimensionLoadResult();

        var clientes = await ExtraerClientesAsync(cancellationToken);
        var productos = await ExtraerProductosAsync(cancellationToken);
        var fuentes = await ExtraerFuentesAsync(cancellationToken);
        var fechas = await ExtraerFechasAsync(cancellationToken);
        var campanas = ObtenerCatalogoCampanas();

        await using var dwConn = new SqlConnection(_settings.AnalyticalDbConnection);
        await dwConn.OpenAsync(cancellationToken);

        await using var tx = (SqlTransaction)await dwConn.BeginTransactionAsync(cancellationToken);
        try
        {
            resultado.ClientesProcesados = await CargarDimClientesAsync(dwConn, tx, clientes, cancellationToken);
            resultado.ProductosProcesados = await CargarDimProductosAsync(dwConn, tx, productos, cancellationToken);
            resultado.FuentesProcesadas = await CargarDimFuentesAsync(dwConn, tx, fuentes, cancellationToken);
            resultado.FechasProcesadas = await CargarDimTiempoAsync(dwConn, tx, fechas, cancellationToken);
            resultado.CampanasProcesadas = await CargarDimCampanasAsync(dwConn, tx, campanas, cancellationToken);

            await tx.CommitAsync(cancellationToken);
            _logger.LogInfo(
                $"[DimensionLoader] Dimensiones cargadas | Clientes: {resultado.ClientesProcesados}, " +
                $"Productos: {resultado.ProductosProcesados}, Fuentes: {resultado.FuentesProcesadas}, " +
                $"Tiempo: {resultado.FechasProcesadas}, Campañas: {resultado.CampanasProcesadas}.");
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            _logger.LogError("[DimensionLoader] Error cargando dimensiones. Se realizó Rollback.", ex);
            throw;
        }

        return resultado;
    }

    private async Task<List<DimClienteDto>> ExtraerClientesAsync(CancellationToken ct)
    {
        var lista = new List<DimClienteDto>
        {
            new(0, 0, "Cliente Desconocido", "N/A", "N/A", "Anónimo", "N/A")
        };

        const string query = @"
            SELECT 
                c.IdCliente,
                c.Nombre,
                c.Email,
                COUNT(o.IdOpinion) AS TotalOpiniones
            FROM Clientes c
            LEFT JOIN Opiniones o ON c.IdCliente = o.IdCliente
            GROUP BY c.IdCliente, c.Nombre, c.Email;";

        await using var conn = new SqlConnection(_settings.RelationalDbConnection);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(query, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            int id = Convert.ToInt32(reader["IdCliente"]);
            string nombre = reader["Nombre"]?.ToString()?.Trim() ?? "Sin Nombre";
            string? email = reader["Email"] == DBNull.Value ? "N/A" : reader["Email"].ToString()?.Trim();
            int totalOpiniones = Convert.ToInt32(reader["TotalOpiniones"]);
            string tipoCliente = totalOpiniones >= 3 ? "Frecuente" : "Regular";

            lista.Add(new DimClienteDto(id, id, nombre, email, "No especificado", tipoCliente, "No especificado"));
        }

        return lista;
    }
    private async Task<List<DimProductoDto>> ExtraerProductosAsync(CancellationToken ct)
    {
        var lista = new List<DimProductoDto>
        {
            new(0, 0, "Producto Desconocido", "Sin Categoría")
        };

        const string query = "SELECT IdProducto, Nombre, Categoria FROM Productos;";

        await using var conn = new SqlConnection(_settings.RelationalDbConnection);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(query, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            int id = Convert.ToInt32(reader["IdProducto"]);
            string nombre = reader["Nombre"]?.ToString()?.Trim() ?? "Sin Nombre";
            string categoria = reader["Categoria"] == DBNull.Value
                ? "General"
                : reader["Categoria"].ToString()!.Trim();

            lista.Add(new DimProductoDto(id, id, nombre, categoria));
        }

        return lista;
    }

    private async Task<List<DimFuenteDatosDto>> ExtraerFuentesAsync(CancellationToken ct)
    {
        var lista = new List<DimFuenteDatosDto>();
        const string query = "SELECT IdFuente, TipoFuente FROM FuenteDatos;";

        await using var conn = new SqlConnection(_settings.RelationalDbConnection);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(query, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            int id = Convert.ToInt32(reader["IdFuente"]);
            string tipo = reader["TipoFuente"]?.ToString()?.Trim() ?? "Desconocida";
            string canal = DerivarCanal(tipo);

            lista.Add(new DimFuenteDatosDto(id, tipo, canal));
        }

        if (lista.Count == 0)
        {
            lista.Add(new DimFuenteDatosDto(1, "Encuestas Internas CSV", "Interno"));
            lista.Add(new DimFuenteDatosDto(2, "Redes Sociales API", "Redes Sociales"));
            lista.Add(new DimFuenteDatosDto(3, "Reseñas Web", "Web"));
        }

        return lista;
    }

    private async Task<List<DimTiempoDto>> ExtraerFechasAsync(CancellationToken ct)
    {
        var fechasUnicas = new HashSet<DateTime>();

        await using (var dwConn = new SqlConnection(_settings.AnalyticalDbConnection))
        {
            await dwConn.OpenAsync(ct);
            const string qStaging = "SELECT DISTINCT CAST(Fecha AS DATE) AS Fecha FROM dbo.Staging_Opiniones WHERE Fecha IS NOT NULL;";
            await using var cmd = new SqlCommand(qStaging, dwConn);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                fechasUnicas.Add(Convert.ToDateTime(reader["Fecha"]).Date);
            }
        }

        await using (var relConn = new SqlConnection(_settings.RelationalDbConnection))
        {
            await relConn.OpenAsync(ct);
            const string qRel = "SELECT DISTINCT CAST(Fecha AS DATE) AS Fecha FROM Opiniones WHERE Fecha IS NOT NULL;";
            await using var cmd = new SqlCommand(qRel, relConn);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                fechasUnicas.Add(Convert.ToDateTime(reader["Fecha"]).Date);
            }
        }

        if (fechasUnicas.Count == 0)
            fechasUnicas.Add(DateTime.Today);

        var culturaEs = new CultureInfo("es-ES");

        return fechasUnicas
            .OrderBy(f => f)
            .Select(fecha =>
            {
                int idTiempo = (fecha.Year * 10000) + (fecha.Month * 100) + fecha.Day;
                int trimestre = ((fecha.Month - 1) / 3) + 1;
                string nombreMes = culturaEs.TextInfo.ToTitleCase(fecha.ToString("MMMM", culturaEs));

                return new DimTiempoDto(idTiempo, fecha, fecha.Day, fecha.Month, fecha.Year, trimestre, nombreMes);
            })
            .ToList();
    }

    private static List<DimCampanaDto> ObtenerCatalogoCampanas() =>
    [
        new(0, "Sin Campaña", "Orgánico"),
        new(1, "Lanzamiento de Temporada", "Lanzamiento"),
        new(2, "Campaña Black Friday", "Promoción"),
        new(3, "Encuesta de Satisfacción Anual", "Fidelización"),
        new(4, "Impulso Redes Sociales", "Marketing Digital")
    ];

    private static string DerivarCanal(string tipoFuente)
    {
        string t = tipoFuente.ToLowerInvariant();
        if (t.Contains("csv") || t.Contains("encuesta") || t.Contains("survey")) return "Interno";
        if (t.Contains("web") || t.Contains("reseña") || t.Contains("review")) return "Web";
        if (t.Contains("red") || t.Contains("social") || t.Contains("api")) return "Redes Sociales";
        return "Otros";
    }

    private static async Task<int> CargarDimClientesAsync(SqlConnection conn, SqlTransaction tx, List<DimClienteDto> clientes, CancellationToken ct)
    {
        const string mergeSql = @"
            MERGE dbo.Dim_Clientes AS target
            USING (SELECT @IdCliente AS IdCliente) AS source
            ON target.IdCliente = source.IdCliente
            WHEN MATCHED THEN
                UPDATE SET
                    IdClienteTransaccional = @IdTrans,
                    Nombre = @Nombre,
                    Email = @Email,
                    Pais = @Pais,
                    TipoCliente = @TipoCliente,
                    RangoEdad = @RangoEdad
            WHEN NOT MATCHED THEN
                INSERT (IdCliente, IdClienteTransaccional, Nombre, Email, Pais, TipoCliente, RangoEdad)
                VALUES (@IdCliente, @IdTrans, @Nombre, @Email, @Pais, @TipoCliente, @RangoEdad);";

        foreach (var c in clientes)
        {
            await using var cmd = new SqlCommand(mergeSql, conn, tx);
            cmd.Parameters.AddWithValue("@IdCliente", c.IdCliente);
            cmd.Parameters.AddWithValue("@IdTrans", c.IdClienteTransaccional);
            cmd.Parameters.AddWithValue("@Nombre", c.Nombre);
            cmd.Parameters.AddWithValue("@Email", (object?)c.Email ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Pais", c.Pais);
            cmd.Parameters.AddWithValue("@TipoCliente", c.TipoCliente);
            cmd.Parameters.AddWithValue("@RangoEdad", c.RangoEdad);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        return clientes.Count;
    }

    private static async Task<int> CargarDimProductosAsync(SqlConnection conn, SqlTransaction tx, List<DimProductoDto> productos, CancellationToken ct)
    {
        const string mergeSql = @"
            MERGE dbo.Dim_Productos AS target
            USING (SELECT @IdProducto AS IdProducto) AS source
            ON target.IdProducto = source.IdProducto
            WHEN MATCHED THEN
                UPDATE SET
                    IdProductoTransaccional = @IdTrans,
                    Nombre = @Nombre,
                    Categoria = @Categoria
            WHEN NOT MATCHED THEN
                INSERT (IdProducto, IdProductoTransaccional, Nombre, Categoria)
                VALUES (@IdProducto, @IdTrans, @Nombre, @Categoria);";

        foreach (var p in productos)
        {
            await using var cmd = new SqlCommand(mergeSql, conn, tx);
            cmd.Parameters.AddWithValue("@IdProducto", p.IdProducto);
            cmd.Parameters.AddWithValue("@IdTrans", p.IdProductoTransaccional);
            cmd.Parameters.AddWithValue("@Nombre", p.Nombre);
            cmd.Parameters.AddWithValue("@Categoria", p.Categoria);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        return productos.Count;
    }

    private static async Task<int> CargarDimFuentesAsync(SqlConnection conn, SqlTransaction tx, List<DimFuenteDatosDto> fuentes, CancellationToken ct)
    {
        const string mergeSql = @"
            MERGE dbo.Dim_FuenteDatos AS target
            USING (SELECT @IdFuente AS IdFuente) AS source
            ON target.IdFuente = source.IdFuente
            WHEN MATCHED THEN
                UPDATE SET
                    TipoFuente = @TipoFuente,
                    Canal = @Canal
            WHEN NOT MATCHED THEN
                INSERT (IdFuente, TipoFuente, Canal)
                VALUES (@IdFuente, @TipoFuente, @Canal);";

        foreach (var f in fuentes)
        {
            await using var cmd = new SqlCommand(mergeSql, conn, tx);
            cmd.Parameters.AddWithValue("@IdFuente", f.IdFuente);
            cmd.Parameters.AddWithValue("@TipoFuente", f.TipoFuente);
            cmd.Parameters.AddWithValue("@Canal", f.Canal);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        return fuentes.Count;
    }

    private static async Task<int> CargarDimTiempoAsync(SqlConnection conn, SqlTransaction tx, List<DimTiempoDto> fechas, CancellationToken ct)
    {
        const string mergeSql = @"
            MERGE dbo.Dim_Tiempo AS target
            USING (SELECT @IdTiempo AS IdTiempo) AS source
            ON target.IdTiempo = source.IdTiempo
            WHEN MATCHED THEN
                UPDATE SET
                    Fecha = @Fecha,
                    Dia = @Dia,
                    Mes = @Mes,
                    Anio = @Anio,
                    Trimestre = @Trimestre,
                    NombreMes = @NombreMes
            WHEN NOT MATCHED THEN
                INSERT (IdTiempo, Fecha, Dia, Mes, Anio, Trimestre, NombreMes)
                VALUES (@IdTiempo, @Fecha, @Dia, @Mes, @Anio, @Trimestre, @NombreMes);";

        foreach (var t in fechas)
        {
            await using var cmd = new SqlCommand(mergeSql, conn, tx);
            cmd.Parameters.AddWithValue("@IdTiempo", t.IdTiempo);
            cmd.Parameters.AddWithValue("@Fecha", t.Fecha);
            cmd.Parameters.AddWithValue("@Dia", t.Dia);
            cmd.Parameters.AddWithValue("@Mes", t.Mes);
            cmd.Parameters.AddWithValue("@Anio", t.Anio);
            cmd.Parameters.AddWithValue("@Trimestre", t.Trimestre);
            cmd.Parameters.AddWithValue("@NombreMes", t.NombreMes);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        return fechas.Count;
    }

    private static async Task<int> CargarDimCampanasAsync(SqlConnection conn, SqlTransaction tx, List<DimCampanaDto> campanas, CancellationToken ct)
    {
        const string mergeSql = @"
            MERGE dbo.Dim_Campanas AS target
            USING (SELECT @IdCampana AS IdCampana) AS source
            ON target.IdCampana = source.IdCampana
            WHEN MATCHED THEN
                UPDATE SET
                    NombreCampana = @NombreCampana,
                    TipoEvento = @TipoEvento
            WHEN NOT MATCHED THEN
                INSERT (IdCampana, NombreCampana, TipoEvento)
                VALUES (@IdCampana, @NombreCampana, @TipoEvento);";

        foreach (var c in campanas)
        {
            await using var cmd = new SqlCommand(mergeSql, conn, tx);
            cmd.Parameters.AddWithValue("@IdCampana", c.IdCampana);
            cmd.Parameters.AddWithValue("@NombreCampana", c.NombreCampana);
            cmd.Parameters.AddWithValue("@TipoEvento", c.TipoEvento);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        return campanas.Count;
    }
}