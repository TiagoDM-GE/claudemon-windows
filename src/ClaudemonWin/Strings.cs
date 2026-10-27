using System.Globalization;
using ClaudemonWin.Core;

namespace ClaudemonWin;

/// <summary>UI text. Portuguese when Windows is in Portuguese, English otherwise.</summary>
internal static class Strings
{
    private static readonly bool Pt =
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("pt", StringComparison.OrdinalIgnoreCase);

    private static string T(string pt, string en) => Pt ? pt : en;

    public static string AppTitle => T("Uso do Claude", "Claude Usage");

    public static string MetricLabel(UsageMetric m) => m.Kind switch
    {
        UsageKind.Session => T("Sessão atual (5h)", "Current session (5h)"),
        UsageKind.WeekAll => T("Semana atual (todos os modelos)", "Current week (all models)"),
        _ => T($"Semana atual ({m.ModelName})", $"Current week ({m.ModelName})"),
    };

    public static string ShortLabel(UsageMetric m) => m.Kind switch
    {
        UsageKind.Session => T("Sessão", "Session"),
        UsageKind.WeekAll => T("Semana", "Week"),
        _ => m.ModelName,
    };

    public static string Used(int pct) => T($"{pct}% usado", $"{pct}% used");

    public static string Countdown(string? raw) => raw == "now" ? T("agora", "now") : raw ?? "";

    public static string ResetsIn(string countdown, string? absolute) =>
        absolute is null ? T($"Repõe em {countdown}", $"Resets in {countdown}")
                         : T($"Repõe em {countdown} · {absolute}", $"Resets in {countdown} · {absolute}");

    public static string ResetsInShort(string countdown) => T($"repõe em {countdown}", $"resets in {countdown}");

    public static string Updated(string clock) => T($"atualizado {clock}", $"updated {clock}");
    public static string Stale => T(" · desatualizado", " · stale");
    public static string Refreshing => T("a atualizar…", "refreshing…");
    public static string Waiting => T("A aguardar dados de uso…", "Waiting for usage data…");

    public static string NotInstalledTitle => T("O Claude Code não está instalado", "Claude Code isn't installed");
    public static string NotInstalledBody => T(
        "Instala o Claude Code e inicia sessão. Por exemplo:\nnpm install -g @anthropic-ai/claude-code",
        "Install Claude Code and sign in. For example:\nnpm install -g @anthropic-ai/claude-code");
    public static string NotInstalledLink => T("Abrir claude.ai/download", "Open claude.ai/download");

    public static string NotSignedInTitle => T("Inicia sessão no Claude Code", "Sign in to Claude Code");
    public static string NotSignedInBody => T(
        "Corre claude num terminal e usa /login com a tua subscrição.",
        "Run claude in a terminal and use /login with your subscription.");
    public static string NotSignedInLink => T("Abrir terminal com claude", "Open a terminal with claude");

    public static string ErrorTitle => T("Não foi possível ler o uso", "Couldn't read usage");
    public static string StaleErrorTitle => T("A mostrar os últimos valores", "Showing last known values");

    public static string Settings => T("Definições", "Settings");
    public static string TrayIcon => T("Ícone na barra", "Tray icon");
    public static string StyleRing => T("Anel", "Ring");
    public static string StylePercent => "%";
    public static string StyleBars => T("Barras", "Bars");
    public static string BarsSecond => T("2.ª barra", "2nd bar");
    public static string AllModels => T("Todos", "All");
    public static string ThemeLabel => T("Tema", "Theme");
    public static string ThemeSystem => T("Sistema", "System");
    public static string ThemeLight => T("Claro", "Light");
    public static string ThemeDark => T("Escuro", "Dark");
    public static string Floating => T("Janela flutuante", "Floating window");
    public static string PreciseBars => T("Barras precisas", "Precise bar fill");
    public static string StartWithWindows => T("Iniciar com o Windows", "Start with Windows");
    public static string Alerts => T("Alertas de uso", "Usage alerts");
    public static string AlertQuarter => T("Quando restar 25%", "When 25% is left");
    public static string AlertLow => T("Quando restar 5%", "When 5% is left");
    public static string AlertDepleted => T("Quando esgotar", "When used up");
    public static string AlertReset => T("Quando o limite repor", "When a limit resets");

    public static string MenuOpen => T("Abrir painel", "Open panel");
    public static string MenuRefresh => T("Atualizar", "Refresh");
    public static string MenuFloating => Floating;
    public static string MenuCompact => T("Vista compacta", "Compact view");
    public static string MenuHide => T("Esconder", "Hide");
    public static string MenuStartup => StartWithWindows;
    public static string MenuDataFolder => T("Abrir pasta de dados", "Open data folder");
    public static string MenuQuit => T("Sair", "Quit");

    public static string AlertBody(UsageAlertThreshold t) => t switch
    {
        UsageAlertThreshold.Quarter => T("Restam 25% do limite.", "25% of your limit remaining."),
        UsageAlertThreshold.Low => T("Só restam 5% do limite.", "Only 5% of your limit left."),
        _ => T("Limite atingido — a quota esgotou.", "Limit reached — your quota is used up."),
    };

    public static string ResetBody => T("Limite reposto — a quota está disponível outra vez.", "Limit reset — your quota is available again.");

    public static string FirstRunTitle => T("O Claudemon está a correr", "Claudemon is running");
    public static string FirstRunBody => T(
        "Clica no ícone para ver o uso. Para o manter visível, arrasta-o da área ^ para a barra de tarefas.",
        "Click the icon to see your usage. To keep it visible, drag it from the ^ overflow onto the taskbar.");
}
