namespace PS5PayloadSender;

public static class ThemeService
{
    private static readonly (Color Accent, Color AccentDim, string Name)[] Themes =
    {
        (Color.FromArgb("#FFD700"), Color.FromArgb("#B8860B"), "ذهبي"),
        (Color.FromArgb("#00BFFF"), Color.FromArgb("#4682B4"), "أزرق"),
        (Color.FromArgb("#32CD32"), Color.FromArgb("#228B22"), "أخضر"),
        (Color.FromArgb("#FF69B4"), Color.FromArgb("#C71585"), "وردي"),
        (Color.FromArgb("#FF4500"), Color.FromArgb("#B22222"), "برتقالي"),
    };

    public static Color Accent => Themes[CurrentIndex].Accent;
    public static Color AccentDim => Themes[CurrentIndex].AccentDim;

    private static int CurrentIndex =>
        Math.Clamp(Preferences.Get("theme_index", 0), 0, Themes.Length - 1);

    public static string ThemeName => Themes[CurrentIndex].Name;

    public static void Next()
    {
        int next = (CurrentIndex + 1) % Themes.Length;
        Preferences.Set("theme_index", next);
    }

    public static void Apply(Page page)
    {
        ApplyToTree(page, Accent, AccentDim);

        if (Shell.Current != null)
        {
            Shell.SetTabBarBackgroundColor(Shell.Current, Color.FromArgb("#0A0A0A"));
            Shell.SetTabBarForegroundColor(Shell.Current, Accent);
            Shell.SetTabBarUnselectedColor(Shell.Current, AccentDim);
            Shell.SetForegroundColor(Shell.Current, Accent);
        }
    }

    private static void ApplyToTree(object element, Color accent, Color dim)
    {
        if (element is Page p)
        {
            p.BackgroundColor = Color.FromArgb("#0A0A0A");
        }
        else if (element is Label l && l.TextColor != null)
        {
            l.TextColor = ToThemeColor(l.TextColor, accent, dim);
        }
        else if (element is Border b)
        {
            if (b.Stroke is SolidColorBrush sb && IsGoldLike(sb.Color))
                b.Stroke = new SolidColorBrush(accent);
        }
        else if (element is Button btn)
        {
            if (btn.BackgroundColor != null && IsGoldLike(btn.BackgroundColor))
                btn.BackgroundColor = accent;
        }
        else if (element is Entry e && e.TextColor != null)
        {
            e.TextColor = ToThemeColor(e.TextColor, accent, dim);
        }
        else if (element is ProgressBar pb)
        {
            pb.ProgressColor = accent;
        }

        if (element is IVisualTreeElement vte)
        {
            foreach (var child in vte.GetVisualChildren())
            {
                if (child is not null)
                    ApplyToTree(child, accent, dim);
            }
        }
    }

    private static bool IsGoldLike(Color c)
    {
        return c == Color.FromArgb("#FFD700") || c == Color.FromArgb("#B8860B")
            || c == Color.FromArgb("#8B6914") || c == Color.FromArgb("#DAA520");
    }

    private static Color ToThemeColor(Color c, Color accent, Color dim)
    {
        if (c == Color.FromArgb("#FFD700")) return accent;
        if (c == Color.FromArgb("#B8860B")) return dim;
        if (c == Color.FromArgb("#8B6914")) return dim;
        if (c == Color.FromArgb("#DAA520")) return dim;
        return c;
    }
}