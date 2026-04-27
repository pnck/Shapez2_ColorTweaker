using JetBrains.Annotations;
using UnityEngine;


namespace ColorTweaker;

using ILogger = Core.Logging.ILogger;

[UsedImplicitly]
public class ModEntry : IMod
{
    private readonly ColorRenderHook _hook;
    private readonly PauseMenuTestItemHook _pauseMenuHook;

    public ModEntry(ILogger logger)
    {
        _hook = new ColorRenderHook(logger);
        _pauseMenuHook = new PauseMenuTestItemHook(logger);

        ColorOverrides.Set('r', new Color(.82f, .07f, .08f));
        ColorOverrides.Set('g', new Color(.09f, .55f, .06f));
        ColorOverrides.Set('b', new Color(.20f, .37f, .89f));
        ColorOverrides.Set('y', new Color(.97f, .85f, .06f));
        ColorOverrides.Set('c', new Color(.09f, .95f, .85f));
        ColorOverrides.Set('m', new Color(.89f, .26f, .86f));

        logger.Info?.Log("ColorTweaker loaded.");
    }

    public void Dispose()
    {
        _pauseMenuHook.Dispose();
        _hook.Dispose();
    }
}
