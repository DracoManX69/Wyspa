using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Wyspa.App.ViewModels;

namespace Wyspa.App;

public partial class NotesTranscriptView : System.Windows.Controls.UserControl
{
    private NotesViewModel? _observed;
    private bool _scrollQueued;
    public NotesTranscriptView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Observe(); Loaded += (_, _) => Observe();
        Unloaded += (_, _) => { if (_observed is not null) _observed.Bubbles.CollectionChanged -= Updated; _observed = null; };
    }
    private void Observe()
    {
        if (_observed is not null) _observed.Bubbles.CollectionChanged -= Updated;
        _observed = DataContext as NotesViewModel;
        if (_observed is not null) _observed.Bubbles.CollectionChanged += Updated;
    }
    private void Updated(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_scrollQueued) return;
        var scroll = FindScroll(Conversation);
        if (scroll is not null && scroll.ScrollableHeight - scroll.VerticalOffset > 60) return;
        _scrollQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _scrollQueued = false;
            if (Conversation.Items.Count > 0) Conversation.ScrollIntoView(Conversation.Items[^1]);
        });
    }
    private static ScrollViewer? FindScroll(DependencyObject parent)
    {
        if (parent is ScrollViewer viewer) return viewer;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            if (FindScroll(VisualTreeHelper.GetChild(parent, i)) is { } child) return child;
        return null;
    }
}
