using WingetAgent.Models;

namespace WingetAgent.Enrichment;

/// <summary>
/// Heuristic classification of a package into a risk-relevant category, plus its
/// publisher (first Id segment) and whether that publisher is well-known. Pure
/// string heuristics on the package Id — good enough to bias the safety score.
/// </summary>
public static class CategoryClassifier
{
    static readonly HashSet<string> Trusted = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft", "Google", "Mozilla", "Apple", "Adobe", "NVIDIA", "Intel", "AMD",
        "Valve", "JetBrains", "Docker", "Git", "Python", "OpenJS", "Nodejs", "Oracle",
        "Canonical", "Dell", "Logitech", "VideoLAN", "GitHub", "Notepad++", "7zip",
        "Igor.Pavlov", "Microsoft.Corporation", "Amazon", "Zoom", "Cisco", "Synology",
    };

    public static (Category category, string publisher, bool trusted) Classify(string id, string name)
    {
        var publisher = id.Contains('.') ? id.Split('.')[0] : id;
        var trusted = Trusted.Contains(publisher);
        var lid = id.ToLowerInvariant();

        Category cat;
        if (lid.Contains("driver") || lid.Contains("nvidia") || lid.Contains("geforce")
            || lid.Contains("realtek") || lid.Contains(".chipset"))
            cat = Category.Driver;
        else if (lid.Contains("redist") || lid.Contains("runtime") || lid.Contains("dotnet")
            || lid.Contains("vcredist") || lid.Contains("directx") || lid.Contains("powershell")
            || lid.Contains("webview") || lid.Contains("framework") || lid.Contains("xnaframework"))
            cat = Category.System;
        else if (lid.Contains("google.chrome") || lid.Contains("mozilla.firefox")
            || lid.Contains("microsoft.edge") || lid.Contains("brave.brave")
            || lid.Contains("opera") || lid.Contains("vivaldi"))
            cat = Category.Browser;
        else if (lid.Contains("git.git") || lid.Contains("python") || lid.Contains("nodejs")
            || lid.Contains("docker") || lid.Contains("visualstudio") || lid.Contains("jetbrains")
            || lid.Contains("azurecli") || lid.Contains("kubernetes") || lid.Contains("golang")
            || lid.Contains("rustlang") || lid.Contains("microsoft.visualstudiocode"))
            cat = Category.Developer;
        else
            cat = Category.Application;

        return (cat, publisher, trusted);
    }
}
