namespace Basin.Capabilities.Defaults;

/// <summary>
/// A token that <see cref="DefaultActivationTokens"/> accepted for a surface.
/// </summary>
/// <param name="Token">The token string.</param>
/// <param name="Surface">The surface that asked to be activated.</param>
/// <param name="FromLaunch"><see langword="true"/> when the shell minted the token with <see cref="DefaultActivationTokens.MintLaunch"/>, <see langword="false"/> when a client minted it.</param>
/// <param name="AppId">The app id that was stored with the token, or <see langword="null"/>.</param>
/// <param name="RequesterPid">The process ID of the client that minted the token, or 0 for a launch token or unknown credentials.</param>
public readonly record struct ActivationRedemption(string Token, Surface Surface, bool FromLaunch, string? AppId, int RequesterPid);
