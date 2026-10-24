using Avalonia.Media.Imaging;
using Basin;
using Basin.Diagnostics;
using Basin.Greetd;
using Basin.Greetd.Host;
using BasinGreeter.UI;

namespace BasinGreeter;

internal sealed partial class Greeter
{
    private GreeterFlow? _flow;
    private CompositorSynchronizationContext? _context;
    private bool _syncingSession;

    internal IReadOnlyList<LoginUser> Users => _flow?.Users ?? [];

    internal IReadOnlyList<GreetdSession> Sessions => _flow?.Sessions ?? [];

    internal GreeterFlow? Flow => _flow;

    internal LoginModel Model => _model;

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
        _model.UsernameEntered += _flow.EnterName;
        _model.Answered += text => _flow.Answer(text);
        _model.Cancelled += _flow.Cancel;
        _model.Dismissed += () =>
        {
            _model.ClearError();
            _flow.Dismiss();
        };
        _model.AllowCommand = _config.Settings.AllowCommand;
        _model.SessionChosen += entry =>
        {
            if (!_syncingSession && entry is not null)
            {
                _flow.SelectSession(entry.Id, byUser: true);
            }
        };
        _model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LoginModel.CommandText))
            {
                _flow.Command = _model.CommandText;
            }
        };
        _flow.Changed += () =>
        {
            if (_model.CommandText != _flow.Command)
            {
                _model.CommandText = _flow.Command;
            }
        };
        _model.Bar.LayoutChosen += id => _flow.SelectLayout(id, byUser: true);
        ShowLayoutList();

        _flow.Start();
        _context = new CompositorSynchronizationContext(_host.Loop);
        WireStatus();
    }

    private void ShowLayoutList()
    {
        _model.Bar.ShowLayouts(_config.Settings.Layouts);
        if (_config.Settings.Layouts.Count == 0)
        {
            _model.Bar.ShowLayout(null);
        }
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
                        _model.Users.Add(new UserEntry(user.Name, user.DisplayName, LoadAvatar(user.IconPath)));
                    }
                }

                _model.Users.Add(new UserEntry("", GreeterText.Other, null) { IsOther = true });
                break;
            case GreeterStepKind.SessionsRead:
                _model.Sessions.Clear();
                foreach (var session in _flow!.Sessions)
                {
                    _model.Sessions.Add(new SessionEntry(session.Id, session.Name));
                }

                break;
            case GreeterStepKind.UsersShown:
                _model.ShowUsers(_model.Users.FirstOrDefault(user => user.Name == step.Text && !user.IsOther));
                break;
            case GreeterStepKind.AskingName:
                _model.AskForUsername();
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
                    var display = _model.CurrentUser;
                    _model.BeginConversation(display);
                    _model.ShowError(incorrect, needsDismiss: false);
                }

                break;
            case GreeterStepKind.Failed:
                _model.ShowError(step.Text ?? "", needsDismiss: true);
                break;
            case GreeterStepKind.NoSession:
                _model.ShowError(step.Text ?? "", needsDismiss: true);
                break;
            case GreeterStepKind.SessionSelected:
                _syncingSession = true;
                try
                {
                    _model.SelectedSession = _model.Sessions.FirstOrDefault(session => session.Id == step.Text);
                }
                finally
                {
                    _syncingSession = false;
                }

                break;
            case GreeterStepKind.LayoutApplied:
            case GreeterStepKind.LayoutFailed:
                _model.Bar.ShowLayout(step.Text);
                break;
            case GreeterStepKind.LoginOk:
                _model.ShowSigningIn();
                break;
        }
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
            return Bitmap.DecodeToWidth(stream, 80);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            _log.Debug(error, $"avatar: cannot load {path}");
            return null;
        }
    }
}
