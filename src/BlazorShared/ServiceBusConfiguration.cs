namespace BlazorShared;

public class ServiceBusConfiguration
{
    public const string CONFIG_NAME = "ServiceBus";

    public string ConnectionString { get; set; }

    public string QueueName { get; set; }
}