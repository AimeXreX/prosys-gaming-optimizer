using System.Windows;
using System.Windows.Controls;

namespace ProSyS.App;

/// <summary>Applies translations to the WPF tree. The translation table itself lives in UiLocalization.Translations.cs.</summary>
public sealed partial class UiLocalization
{
    private readonly Dictionary<DependencyObject, string> _originals = new();

    public void Apply(DependencyObject root, bool usePersian)
    {
        foreach (var element in Walk(root))
        {
            if (element is TextBlock text) ApplyValue(text, text.Text, value => text.Text = value, usePersian);
            if (element is ContentControl content && content.Content is string value) ApplyValue(content, value, translated => content.Content = translated, usePersian);
            if (element is FrameworkElement framework && framework.ToolTip is string tip) ApplyToolTip(framework, tip, usePersian);
        }
    }

    private void ApplyValue(DependencyObject owner, string current, Action<string> setter, bool usePersian)
    {
        if (!_originals.TryGetValue(owner, out var original)) { original = current; _originals[owner] = original; }
        setter(Translate(original, usePersian));
    }

    private void ApplyToolTip(FrameworkElement owner, string current, bool usePersian)
    {
        if (!_originals.TryGetValue(owner, out var original)) { original = current; _originals[owner] = original; }
        owner.ToolTip = Translate(original, usePersian);
    }

    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root))
            if (child is DependencyObject dependency)
                foreach (var descendant in Walk(dependency)) yield return descendant;
    }
}
