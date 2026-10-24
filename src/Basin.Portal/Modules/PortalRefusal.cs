using Basin.Capabilities;

namespace Basin.Portal;

internal static class PortalRefusal
{
    public static uint RefusalFor(PromptResponse response) =>
        response == PromptResponse.Denied ? PortalResponse.Cancelled : PortalResponse.Other;

    public static uint RefusalFor(in PromptOutcome<bool> answer) =>
        answer.IsAccepted && !answer.Value ? PortalResponse.Cancelled : RefusalFor(answer.Response);
}
