namespace Basin.Portal.Prompts.Skia;

public interface ISkiaPromptHost
{
    IOutput? OutputFor(string parentWindow);

    ISkiaPromptSurface? Show(SkiaPromptView view, IOutput output, bool cover);
}
