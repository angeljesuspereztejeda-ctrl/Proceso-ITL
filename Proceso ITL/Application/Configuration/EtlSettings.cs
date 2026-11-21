namespace OpinionesETL.Worker.Application.Configuration;

public class EtlSettings
{
    public string CsvPath { get; set; } = string.Empty;
    public string RelationalDbConnection { get; set; } = string.Empty;
    public string AnalyticalDbConnection { get; set; } = string.Empty;
    public string ApiBaseUrl { get; set; } = string.Empty;
    public string ApiEndpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string StagingJsonFolder { get; set; } = "Staging";
    public int ExecutionIntervalSeconds { get; set; } = 300;
}