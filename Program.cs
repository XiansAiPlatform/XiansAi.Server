using DotNetEnv;


public class Program
{
    public static void Main(string[] args)
    {
        Env.Load();

        var builder = WebApplication.CreateBuilder(args);

        // Configure all services
        builder.ConfigureServices();

        var app = builder.Build();

        // Configure middleware pipeline
        app.ConfigureMiddleware();

        app.Run();
    }
}
