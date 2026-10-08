using System.Collections.Generic;
using System.Collections.Immutable;

namespace NekoStrap.Roblox;

/// <summary>
/// Тип значения FastFlag для валидации и UI.
/// </summary>
public enum FastFlagType
{
    Boolean,
    Integer,
    String,
}

/// <summary>
/// Описание известного FastFlag: тип, диапазон/варианты, описание, категория.
/// </summary>
public sealed record FastFlagInfo(
    string Name,
    FastFlagType Type,
    string Description,
    string Category,
    string DefaultValue = "",
    IReadOnlyList<string>? AllowedValues = null,
    int? MinValue = null,
    int? MaxValue = null);

/// <summary>Причина отказа валидации: текст локализуется уже в UI.</summary>
public enum FastFlagError
{
    None,
    UnknownFlag,
    NotBoolean,
    NotInteger,
    BelowMin,
    AboveMax,
    NotAllowed,
}

/// <summary>Результат валидации. Detail — исходное значение, как ввёл пользователь.</summary>
public readonly record struct FastFlagResult(bool Valid, FastFlagError Error, string Detail)
{
    public static FastFlagResult Ok => new(true, FastFlagError.None, "");

    public static FastFlagResult Fail(FastFlagError error, string detail) =>
        new(false, error, detail);
}

/// <summary>
/// Реестр известных FastFlags (allowlist).
/// Используется для валидации, подсказок в UI и типизированных пресетов.
/// </summary>
public static class FastFlagRegistry
{
    private static readonly ImmutableDictionary<string, FastFlagInfo> _flags;

    static FastFlagRegistry()
    {
        var builder = ImmutableDictionary.CreateBuilder<string, FastFlagInfo>();

        // Rendering / Graphics
        Add(builder, "FFlagDebugForceFutureIsBrightPhase3", FastFlagType.Boolean, "Force Future Is Bright (Phase 3) lighting", "Rendering", "false");
        Add(builder, "FFlagDebugForceFutureIsBrightPhase2", FastFlagType.Boolean, "Force Future Is Bright (Phase 2/ShadowMap) lighting", "Rendering", "false");
        Add(builder, "DFFlagDebugRenderForceTechnologyVoxel", FastFlagType.Boolean, "Force Voxel lighting technology", "Rendering", "false");
        Add(builder, "FFlagRenderNoLowFrmBloom", FastFlagType.Boolean, "Disable bloom on low frame rate", "Rendering", "false");
        Add(builder, "FFlagDisablePostFx", FastFlagType.Boolean, "Disable all post-processing effects", "Rendering", "false");
        Add(builder, "FIntDebugForceMSAASamples", FastFlagType.Integer, "Force MSAA sample count (0=off, 2, 4, 8)", "Rendering", "0", allowed: ["0", "2", "4", "8"]);
        Add(builder, "FFlagDebugGraphicsPreferVulkan", FastFlagType.Boolean, "Prefer Vulkan over D3D11", "Rendering", "false");

        // Performance / FPS
        Add(builder, "DFIntTaskSchedulerTargetFps", FastFlagType.Integer, "Target FPS cap for task scheduler (5-1000)", "Performance", "60", min: 5, max: 1000);
        Add(builder, "DFIntCSGLevelOfDetailSwitchingDistance", FastFlagType.Integer, "Distance for CSG level of detail switching", "Performance", "1000", min: 100, max: 10000);
        Add(builder, "FIntFRMMaxGrassDistance", FastFlagType.Integer, "Maximum grass render distance (0=disabled)", "Performance", "200", min: 0, max: 5000);
        Add(builder, "FIntCameraMaxZoomDistance", FastFlagType.Integer, "Maximum camera zoom distance", "Performance", "128", min: 10, max: 5000);
        Add(builder, "DFFlagDisableDPIScale", FastFlagType.Boolean, "Disable DPI scaling (game renders at native resolution)", "Performance", "false");

        // Network / Ping
        Add(builder, "DFFlagDebugPrintDataPingBreakDown", FastFlagType.Boolean, "Print detailed ping breakdown to log", "Network", "false");
        Add(builder, "FFlagDebugPrintNetworkStats", FastFlagType.Boolean, "Print network statistics to log", "Network", "false");

        // Debug / Logging
        Add(builder, "FFlagDebugCheckAudio", FastFlagType.Boolean, "Enable audio debugging checks", "Debug", "false");
        Add(builder, "FFlagDebugGraphicsDisableVulkan", FastFlagType.Boolean, "Force disable Vulkan", "Debug", "false");
        Add(builder, "FFlagDebugGraphicsPreferD3D11", FastFlagType.Boolean, "Prefer D3D11 over Vulkan", "Debug", "false");

        // Input / Camera
        Add(builder, "FIntCameraMinZoomDistance", FastFlagType.Integer, "Minimum camera zoom distance", "Camera", "0.5", min: 0, max: 100);
        Add(builder, "FFlagUserCameraToggle", FastFlagType.Boolean, "Enable camera toggle mode", "Camera", "false");

        // Memory / Cache
        Add(builder, "DFIntTextureMemoryLimitMB", FastFlagType.Integer, "Texture memory limit in MB", "Memory", "512", min: 64, max: 4096);
        Add(builder, "DFIntMeshPartMemoryLimitMB", FastFlagType.Integer, "MeshPart memory limit in MB", "Memory", "256", min: 32, max: 2048);

        // Physics
        Add(builder, "DFIntPhysicsSolverIterations", FastFlagType.Integer, "Physics solver iterations", "Physics", "8", min: 1, max: 32);
        Add(builder, "DFIntPhysicsMaxContacts", FastFlagType.Integer, "Maximum physics contacts per frame", "Physics", "10000", min: 1000, max: 100000);

        // UI / Misc
        Add(builder, "FFlagEnableNewChat", FastFlagType.Boolean, "Enable new chat system", "UI", "true");
        Add(builder, "FFlagLuaDebuggerEnabled", FastFlagType.Boolean, "Enable Lua debugger", "Developer", "false");
        Add(builder, "FFlagDebugShowPingInMenu", FastFlagType.Boolean, "Show ping in escape menu", "UI", "false");

        _flags = builder.ToImmutable();
    }

    private static void Add(
        ImmutableDictionary<string, FastFlagInfo>.Builder builder,
        string name,
        FastFlagType type,
        string description,
        string category,
        string defaultValue = "",
        IReadOnlyList<string>? allowed = null,
        int? min = null,
        int? max = null)
    {
        builder[name] = new FastFlagInfo(name, type, description, category, defaultValue, allowed, min, max);
    }

    /// <summary>Получить информацию о флаге по имени (без учёта регистра).</summary>
    public static bool TryGet(string name, out FastFlagInfo info)
    {
        return _flags.TryGetValue(name, out info!);
    }

    /// <summary>Проверить, известен ли флаг.</summary>
    public static bool IsKnown(string name) => _flags.ContainsKey(name);

    /// <summary>Получить все известные флаги.</summary>
    public static IReadOnlyDictionary<string, FastFlagInfo> All => _flags;

    /// <summary>Получить флаги по категории.</summary>
    public static IEnumerable<FastFlagInfo> GetByCategory(string category)
    {
        return _flags.Values.Where(f => f.Category.Equals(category, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Получить список категорий.</summary>
    public static IEnumerable<string> Categories => _flags.Values.Select(f => f.Category).Distinct().OrderBy(c => c);

    /// <summary>
    /// Валидировать значение флага. Отказ возвращает код + введённое значение,
    /// сообщение для пользователя собирает UI (Core не знает о языках).
    /// </summary>
    public static FastFlagResult Validate(string name, string value)
    {
        if (!TryGet(name, out var info) || info == null)
            return FastFlagResult.Fail(FastFlagError.UnknownFlag, name);

        switch (info.Type)
        {
            case FastFlagType.Boolean:
                if (value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                    value.Equals("false", StringComparison.OrdinalIgnoreCase))
                    return FastFlagResult.Ok;
                return FastFlagResult.Fail(FastFlagError.NotBoolean, value);

            case FastFlagType.Integer:
                if (!int.TryParse(value, out int v))
                    return FastFlagResult.Fail(FastFlagError.NotInteger, value);
                if (info.MinValue.HasValue && v < info.MinValue)
                    return FastFlagResult.Fail(FastFlagError.BelowMin, value);
                if (info.MaxValue.HasValue && v > info.MaxValue)
                    return FastFlagResult.Fail(FastFlagError.AboveMax, value);
                if (info.AllowedValues?.Count > 0 && !info.AllowedValues.Contains(value))
                    return FastFlagResult.Fail(FastFlagError.NotAllowed, value);
                return FastFlagResult.Ok;

            case FastFlagType.String:
                if (info.AllowedValues?.Count > 0 && !info.AllowedValues.Contains(value))
                    return FastFlagResult.Fail(FastFlagError.NotAllowed, value);
                return FastFlagResult.Ok;

            default:
                return FastFlagResult.Ok;
        }
    }

    /// <summary>
    /// Получить строку с описанием для UI (тип + диапазон).
    /// </summary>
    public static string GetHint(FastFlagInfo info)
    {
        return info.Type switch
        {
            FastFlagType.Boolean => "true/false",
            FastFlagType.Integer when info.MinValue.HasValue && info.MaxValue.HasValue => $"{info.MinValue}..{info.MaxValue}",
            FastFlagType.Integer when info.AllowedValues?.Count > 0 => $"{{{string.Join("|", info.AllowedValues)}}}",
            FastFlagType.String when info.AllowedValues?.Count > 0 => $"{{{string.Join("|", info.AllowedValues)}}}",
            _ => info.Type.ToString().ToLowerInvariant()
        };
    }
}