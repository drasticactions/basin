using Avalonia.Media.Imaging;
using Basin.Greetd;

namespace EightWm;

internal sealed partial class EightGreeter
{
    private GreeterFlow? _flow;
    private bool _suggestionTaken;
    private bool _syncing;

    private void WireLogin()
    {
        _flow = _greeter.StartLogin();
        _greeter.Stepped += OnStep;
        _signIn.OptionsAvailable = true;
        _signIn.Submitted += text =>
        {
            if (_signIn.AskName)
            {
                _signIn.Busy = false;
                _flow.EnterName(text);
                return;
            }

            if (!_flow.Answer(text))
            {
                _signIn.Busy = false;
            }
        };
        _signIn.BackRequested += () => _flow.Cancel();
        _signIn.UserChosen += user =>
        {
            if (user.IsOther)
            {
                _flow.ChooseOther();
                return;
            }

            _flow.Choose(user.Name);
        };
        _signIn.SessionChosen += id =>
        {
            if (!_syncing)
            {
                _flow.SelectSession(id, byUser: true);
            }
        };
        _signIn.LayoutChosen += id =>
        {
            if (!_syncing)
            {
                _flow.SelectLayout(id, byUser: true);
            }
        };
        ShowLayoutList();
        _flow.Start();
    }

    private void ShowLayoutList()
    {
        _signIn.Layouts.Clear();
        if (_config.Settings.Layouts.Count < 2)
        {
            return;
        }

        foreach (var layout in _config.Settings.Layouts)
        {
            _signIn.Layouts.Add(new SignInOption(layout, layout) { Chosen = layout == _flow?.SelectedLayout });
        }
    }

    private void OnStep(GreeterStep step)
    {
        switch (step.Kind)
        {
            case GreeterStepKind.UsersRead:
                _signIn.Users.Clear();
                if (_config.Settings.ListUsers)
                {
                    foreach (var user in _flow!.Users)
                    {
                        _signIn.Users.Add(new UserTileModel(user.Name, user.DisplayName, LoadAvatar(user.IconPath)));
                    }
                }

                _signIn.Users.Add(new UserTileModel("", "Other user", null, isOther: true));
                break;
            case GreeterStepKind.SessionsRead:
                _signIn.Sessions.Clear();
                foreach (var session in _flow!.Sessions)
                {
                    _signIn.Sessions.Add(new SignInOption(session.Id, session.Name) { Chosen = session.Id == _flow.SelectedSession });
                }

                break;
            case GreeterStepKind.UsersShown:
                var suggested = _suggestionTaken ? null : _signIn.Users.FirstOrDefault(user => user.Name == step.Text && !user.IsOther)
                    ?? _signIn.Users.FirstOrDefault(user => !user.IsOther);
                _suggestionTaken = true;
                _signIn.ShowUsers(null);
                if (suggested is not null)
                {
                    _flow!.Choose(suggested.Name);
                }
                else if (_signIn.Users.Count == 1)
                {
                    _flow!.ChooseOther();
                }

                break;
            case GreeterStepKind.AskingName:
                _signIn.AskName = true;
                _signIn.BeginConversation(_signIn.Users.FirstOrDefault(user => user.IsOther));
                _signIn.ShowPrompt("User name", secret: false);
                break;
            case GreeterStepKind.ConversationBegun:
                _signIn.AskName = false;
                _signIn.BeginConversation(_signIn.Users.FirstOrDefault(user => user.Name == _flow!.CurrentUser && !user.IsOther)
                    ?? new UserTileModel(_flow!.CurrentUser ?? "", _flow.CurrentDisplayName ?? _flow.CurrentUser ?? "", null));
                break;
            case GreeterStepKind.Message:
                _signIn.Message = step.Text ?? "";
                break;
            case GreeterStepKind.Prompt:
                _signIn.ShowPrompt(step.Text ?? "", step.PromptType == GreetdAuthMessageType.Secret);
                break;
            case GreeterStepKind.AuthFailed:
                if (step.Text is { } incorrect)
                {
                    _signIn.ShowError(incorrect);
                }

                break;
            case GreeterStepKind.Failed:
            case GreeterStepKind.NoSession:
                _signIn.ShowError(step.Text ?? "");
                break;
            case GreeterStepKind.SessionSelected:
                _syncing = true;
                foreach (var option in _signIn.Sessions)
                {
                    option.Chosen = option.Id == step.Text;
                }

                _syncing = false;
                break;
            case GreeterStepKind.LoginOk:
                _signIn.ShowWelcome();
                if (_screen is { } welcome)
                {
                    welcome.KeyboardPanel.IsOpen = false;
                }

                break;
            case GreeterStepKind.LayoutApplied:
            case GreeterStepKind.LayoutFailed:
                _syncing = true;
                foreach (var option in _signIn.Layouts)
                {
                    option.Chosen = option.Id == step.Text;
                }

                _syncing = false;
                break;
        }

        _outputs.ScheduleAll();
    }

    private Bitmap? LoadAvatar(string? path)
    {
        if (path is null)
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            return Bitmap.DecodeToWidth(stream, 192);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            _log.Debug(error, $"login: cannot load the avatar {path}");
            return null;
        }
    }
}
