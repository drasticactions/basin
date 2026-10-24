namespace Basin.Ipc;

internal static class IpcApprovalMethods
{
    public static void Register(IpcServer server)
    {
        if (server.Approvals is not { } broker)
        {
            return;
        }

        broker.Attach(server);
        server.Events.DeclareLibrary(IpcEventNames.ApprovalRequested);
        server.Methods.RegisterLibrary(IpcMethodNames.ApprovalAnswer, (ref IpcParams parameters, IpcReply reply) =>
        {
            if (parameters.Read(IpcJsonContext.Default.IpcApprovalAnswerParams) is not { } request)
            {
                return;
            }

            IpcApprovalAnswer? answer = request.Answer switch
            {
                "allow_once" => IpcApprovalAnswer.AllowOnce,
                "allow_run" => IpcApprovalAnswer.AllowRun,
                "deny" => IpcApprovalAnswer.Deny,
                _ => null,
            };
            if (answer is null)
            {
                reply.Error(IpcErrorCodes.InvalidParams, "'answer' must be allow_once, allow_run or deny.");
                return;
            }

            if (reply.Sink is not IIpcEventSubscriber subscriber
                || !ReferenceEquals(server.Events.FirstSubscriber(IpcEventNames.ApprovalRequested), subscriber))
            {
                reply.Error(IpcErrorCodes.Refused, "This connection cannot answer. Only the first connection that subscribes to approval/requested can answer.");
                return;
            }

            if (!broker.Answer(request.Id, answer.Value))
            {
                reply.Error(IpcErrorCodes.NotFound, $"No approval {request.Id} is pending.");
                return;
            }

            IpcWrite.Empty(reply);
        });
    }
}
