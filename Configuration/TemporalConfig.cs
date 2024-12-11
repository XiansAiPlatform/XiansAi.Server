public class TemporalConfig
{
    public string TemporalServerUrl { get; set; } = "localhost:7233";
    public string Namespace { get; set; } = "default";
    public string TaskQueue { get; set; } = "flowmaxer-queue";
}