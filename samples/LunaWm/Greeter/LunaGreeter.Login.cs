using Basin;
using Basin.Diagnostics;
using Basin.Greetd;

namespace LunaWm;

internal sealed partial class LunaGreeter
{
    private GreeterFlow? _flow;

    internal GreeterFlow? Flow => _flow;

    private void WireLogin()
    {
        _flow = _greeter.StartLogin();
        _greeter.Stepped += OnStep;

        _model.UserChosen += user =>
        {
            if (user.IsOther)
            {
                _flow.ChooseOther();
                return;
            }

            _flow.Choose(user.Name);
        };
        _model.NameEntered += _flow.EnterName;
        _model.Answered += text => _flow.Answer(text);
        _model.Cancelled += _flow.Cancel;
        _model.Dismissed += _flow.Dismiss;
        _flow.Start();
    }

    private void OnStep(GreeterStep step)
    {
        switch (step.Kind)
        {
            case GreeterStepKind.UsersRead:
                _model.Users.Clear();
                if (_config.Settings.ListUsers)
                {
                    foreach (var user in _flow!.Users)
                    {
                        _model.Users.Add(new GreeterUserEntry(user.Name, user.DisplayName, LoadImage(user.IconPath, 144, _log)));
                    }
                }

                _model.Users.Add(new GreeterUserEntry("", "Other", null) { IsOther = true });
                break;
            case GreeterStepKind.UsersShown:
                _model.ShowUserStrip(_model.Users.FirstOrDefault(user => user.Name == step.Text && !user.IsOther)
                    ?? _model.Users.FirstOrDefault(user => !user.IsOther));
                break;
            case GreeterStepKind.AskingName:
                _model.SelectedUser = _model.Users.FirstOrDefault(user => user.IsOther);
                _model.AskForName();
                break;
            case GreeterStepKind.ConversationBegun:
                if (_model.Users.FirstOrDefault(user => user.Name == _flow!.CurrentUser && !user.IsOther) is { } chosen)
                {
                    _model.SelectedUser = chosen;
                }

                _model.BeginConversation(step.Text ?? "");
                break;
            case GreeterStepKind.Message:
                _model.AddMessage(step.Text ?? "");
                break;
            case GreeterStepKind.Prompt:
                _model.ShowPrompt(step.Text ?? "", step.PromptType == GreetdAuthMessageType.Secret);
                break;
            case GreeterStepKind.AuthFailed:
                if (step.Text is { } incorrect)
                {
                    _model.BeginConversation(_model.CurrentUser);
                    _model.ShowError(incorrect, needsDismiss: false, shake: true);
                }

                break;
            case GreeterStepKind.Failed:
                _model.ShowError(step.Text ?? "", needsDismiss: true, shake: true);
                break;
            case GreeterStepKind.NoSession:
                _model.ShowError(step.Text ?? "", needsDismiss: true, shake: false);
                break;
        }

        if (step.Kind is GreeterStepKind.SessionSelected or GreeterStepKind.LayoutApplied && _menu is { Open: true, Kind: "device" })
        {
            FillMenu(rebuild: true);
        }

        _outputs?.ScheduleAll();
    }
}
