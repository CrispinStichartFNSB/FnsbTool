using Microsoft.Extensions.Configuration;

namespace FnsbTool.commands;

using System.CommandLine;
using System.CommandLine.Invocation;
using Microsoft.Extensions.Logging;

public class Test : Command
{
    public Test()
        : base("test", "Debug stuff")
    {
        AddOptions(this);
    }

    public class CommandHandler(ILogger<CommandHandler> logger, IConfiguration configuration)
        : ICommandHandler
    {
        public int Invoke(InvocationContext context)
        {
            Console.WriteLine(
                $"Config: {configuration.GetValue<string>("ConnectionStrings:SQLServer")}"
            );
            return 0;
        }

        public Task<int> InvokeAsync(InvocationContext context)
        {
            return Task.FromResult(Invoke(context));
        }
    }

    static void AddOptions(Command command) { }
}
