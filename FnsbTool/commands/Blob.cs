using System.CommandLine;
using System.CommandLine.Invocation;
using FnsbTool.commands.blob;
using Microsoft.Extensions.Logging;

namespace FnsbTool.commands;

public class Blob : Command
{
    public Blob()
        : base("blob", "Work with azure's blob storage.")
    {
        AddCommand(new Upload());
        AddOptions(this);
    }

    public class CommandHandler(ILogger<CommandHandler> logger) : ICommandHandler
    {
        public int Invoke(InvocationContext context)
        {
            return 0;
        }

        public Task<int> InvokeAsync(InvocationContext context)
        {
            return Task.FromResult(Invoke(context));
        }
    }

    static void AddOptions(Command command)
    {
        var acctName = new Option<string>("--account-name", "Name of storage resource.")
        {
            IsRequired = false,
        };
        var container = new Option<string>("--container", "Name of container.")
        {
            IsRequired = true,
        };

        command.AddGlobalOption(acctName);
        command.AddGlobalOption(container);
    }
}
