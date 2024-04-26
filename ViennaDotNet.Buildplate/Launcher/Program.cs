using CliUtils;
using CliUtils.Exceptions;
using Serilog;
using ViennaDotNet.DB;
using ViennaDotNet.EventBus.Client;
using ViennaDotNet.ObjectStore.Client;

namespace ViennaDotNet.Buildplate.Launcher
{
    internal static class Program
    {
        static void Main(string[] args)
        {
            var log = new LoggerConfiguration()
                .WriteTo.Console()
                .WriteTo.File("logs/debug.txt", rollingInterval: RollingInterval.Day, rollOnFileSizeLimit: true, fileSizeLimitBytes: 8338607, outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .MinimumLevel.Debug()
                .CreateLogger();

            Log.Logger = log;

            Options options = new Options();
            options.addOption(Option.builder()
                .Option("eventbus")
                .LongOpt("eventbus")
                .HasArg()
                .ArgName("eventbus")
                .Desc("Event bus address, defaults to localhost:5532")
                .Build());
            options.addOption(Option.builder()
                .Option("publicAddress")
                .LongOpt("publicAddress")
                .HasArg()
                .ArgName("address")
                .Required()
                .Desc("Public server address to report in instance info")
                .Build());
            options.addOption(Option.builder()
                .Option("bridgeJar")
                .LongOpt("bridgeJar")
                .HasArg()
                .ArgName("jar")
                .Required()
                .Desc("Fountain bridge JAR file")
                .Build());
            options.addOption(Option.builder()
                .Option("serverTemplateDir")
                .LongOpt("serverTemplateDir")
                .HasArg()
                .ArgName("dir")
                .Required()
                .Desc("Minecraft/Fabric server template directory, containing the Fabric JAR, mods, and libraries")
                .Build());
            options.addOption(Option.builder()
                .Option("fabricJarName")
                .LongOpt("fabricJarName")
                .HasArg()
                .ArgName("name")
                .Required()
                .Desc("Name of the Fabric JAR to run within the server template directory")
                .Build());
            options.addOption(Option.builder()
                .Option("connectorPluginJar")
                .LongOpt("connectorPluginJar")
                .HasArg()
                .ArgName("jar")
                .Required()
                .Desc("Fountain connector plugin JAR")
                .Build());

            CommandLine commandLine;
            string eventBusConnectionString;
            string publicAddress;
            string bridgeJar;
            string serverTemplateDir;
            string fabricJarName;
            string connectorPluginJar;
            try
            {
                commandLine = new DefaultParser().parse(options, args);
                eventBusConnectionString = commandLine.hasOption("eventbus") ? commandLine.getOptionValue("eventbus")! : "localhost:5532";
                publicAddress = commandLine.getOptionValue("publicAddress")!;
                bridgeJar = commandLine.getOptionValue("bridgeJar")!;
                serverTemplateDir = commandLine.getOptionValue("serverTemplateDir")!;
                fabricJarName = commandLine.getOptionValue("fabricJarName")!;
                connectorPluginJar = commandLine.getOptionValue("connectorPluginJar")!;
            }
            catch (ParseException exception)
            {
                Log.Fatal(exception.ToString());
                Environment.Exit(1);
                return;
            }

            Log.Information("Connecting to event bus");
            EventBusClient eventBusClient;
            try
            {
                eventBusClient = EventBusClient.create(eventBusConnectionString);
            }
            catch (EventBusClientException exception)
            {
                Log.Fatal($"Could not connect to event bus: {exception}");
                Environment.Exit(1);
                return;
            }
            Log.Information("Connected to event bus");

            Starter starter = new Starter(eventBusClient, eventBusConnectionString, publicAddress, bridgeJar, serverTemplateDir, fabricJarName, connectorPluginJar);
            InstanceManager instanceManager = new InstanceManager(eventBusClient, starter);

            AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
            {
                new Thread(() =>
                {
                    instanceManager.shutdown();
                });
            };
        }
    }
}
