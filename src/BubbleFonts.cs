using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Markup;
using System.Windows.Media;

namespace DragonPet;

public sealed record BubbleFontOption(string Id, string Label, FontFamily Family);

public static class BubbleFonts
{
    public const string DefaultId = "@zcool-kuaile";
    public const double DefaultSize = 16.5;
    static readonly Lazy<IReadOnlyList<BubbleFontOption>> available = new(() =>
    {
        var chinese = XmlLanguage.GetLanguage("zh-CN");
        var preferred = new[] { "Microsoft YaHei UI", "YouYuan", "MiSans", "Noto Sans SC", "KaiTi", "SimSun" };
        return new[] { new BubbleFontOption(DefaultId, "站酷快乐体 · 内置", Ui.PetFont) }
            .Concat(Fonts.SystemFontFamilies
                .Where(f => !f.Source.StartsWith("@", StringComparison.Ordinal))
                .OrderBy(f => { int i = Array.IndexOf(preferred, f.Source); return i < 0 ? int.MaxValue : i; })
                .ThenBy(f => f.Source, StringComparer.CurrentCultureIgnoreCase)
                .Select(f => new BubbleFontOption(f.Source, f.FamilyNames.TryGetValue(chinese, out var local) ? local : f.Source, f)))
            .ToArray();
    });
    public static IReadOnlyList<BubbleFontOption> Available => available.Value;
    public static FontFamily Resolve(string id) => id == DefaultId ? Ui.PetFont :
        Available.FirstOrDefault(f => f.Id.Equals(id, StringComparison.OrdinalIgnoreCase))?.Family ?? Ui.PetFont;
}
