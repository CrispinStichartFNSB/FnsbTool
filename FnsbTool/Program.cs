using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Hosting;
using System.CommandLine.Parsing;
using System.Data.Common;
using Azure.Identity;
using FnsbTool.commands;
using FnsbTool.commands.blob;
using FnsbTool.commands.database;
using FnsbTool.shared;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FnsbTool;

internal class Program
{
    private static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("My Helper App")
        {
            new Blob(),
            new Database(),
            new Test(),
        };
        var verbosityOption = new Option<Verbosity>(
            aliases: ["-v", "--verbose"],
            description: "Set level of output."
        )
        {
            Arity = ArgumentArity.ZeroOrOne,
        };
        verbosityOption.SetDefaultValue(Verbosity.Normal);

        rootCommand.AddGlobalOption(verbosityOption);
        var configOption = new Option<string>("--configuration", "Path to JSON configuration file");

        rootCommand.AddGlobalOption(configOption);

        var cmdlineBuilder = new CommandLineBuilder(rootCommand);

        var parser = cmdlineBuilder
            .UseHost(
                _ => Host.CreateDefaultBuilder(args),
                builder =>
                {
                    builder.UseCommandHandler<Upload, Upload.CommandHandler>();
                    builder.UseCommandHandler<Export, Export.CommandHandler>();
                    builder.UseCommandHandler<Import, Import.CommandHandler>();
                    builder.UseCommandHandler<Test, Test.CommandHandler>();

                    builder.ConfigureLogging(
                        (ctx, logging) =>
                        {
                            /* TODO: figure out if this is a good idea. */
                            // remove the default providers so we control exactly what’s shown.
                            logging.ClearProviders();
                            logging.AddSimpleConsole(o => o.SingleLine = true);

                            var invocation = ctx.GetInvocationContext();
                            var verbosity = invocation.ParseResult.GetValueForOption(
                                verbosityOption
                            );

                            var minimum = verbosity switch
                            {
                                Verbosity.Silent => LogLevel.None,
                                Verbosity.Quiet => LogLevel.Error,
                                Verbosity.Normal => LogLevel.Information,
                                Verbosity.Detailed => LogLevel.Debug,
                                Verbosity.Debug => LogLevel.Trace,
                                _ => LogLevel.Information,
                            };

                            logging.SetMinimumLevel(minimum);
                        }
                    );

                    builder.ConfigureAppConfiguration(
                        (ctx, configBuilder) =>
                        {
                            const string configFilename = "fnsb_settings.json";
                            configBuilder.AddJsonFile(
                                Path.Join(
                                    Environment.GetFolderPath(
                                        Environment.SpecialFolder.UserProfile
                                    ),
                                    configFilename
                                ),
                                true
                            );

                            var invocation = ctx.GetInvocationContext();
                            var parse = invocation.ParseResult;

                            var file = parse.GetValueForOption(configOption);
                            if (!string.IsNullOrWhiteSpace(file))
                            {
                                configBuilder.AddJsonFile(
                                    file,
                                    optional: false,
                                    reloadOnChange: true
                                );
                            }
                        }
                    );

                    builder.ConfigureServices(
                        (ctx, s) =>
                        {
                            s.AddSingleton<SqlConnection>(_ =>
                            {
                                DbConnectionStringBuilder connectionStringBuilder = new()
                                {
                                    ConnectionString = ctx.Configuration.GetConnectionString(
                                        "SQLServer"
                                    ),
                                };

                                var overrides = ctx.Configuration.GetSection(
                                    "SqlConnectionStringOverrides"
                                );
                                foreach (var k in overrides.GetChildren())
                                {
                                    connectionStringBuilder[k.Key] = k.Value;
                                }

                                return new SqlConnection(connectionStringBuilder.ConnectionString);
                            });
                            s.AddAzureClients(clientBuilder =>
                            {
                                var tenantId = ctx.Configuration.GetValue<string>(
                                    "azure:servicePrincipal:tenantId"
                                );
                                var clientId = ctx.Configuration.GetValue<string>(
                                    "azure:servicePrincipal:clientId"
                                );
                                var clientSecret = ctx.Configuration.GetValue<string>(
                                    "azure:servicePrincipal:clientSecret"
                                );

                                var blobStorageAccountName = ctx.Configuration.GetValue<string>(
                                    "azure:resources:propertySearch:blobStorageAccountName"
                                );

                                clientBuilder.AddBlobServiceClient(
                                    new Uri(
                                        $"https://{blobStorageAccountName}.blob.core.windows.net"
                                    )
                                );
                                clientBuilder.UseCredential(
                                    new ClientSecretCredential(tenantId, clientId, clientSecret)
                                );
                            });
                        }
                    );
                }
            )
            .UseDefaults()
            .Build();

        return await parser.InvokeAsync(args);
    }
}
