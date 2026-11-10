using System.Collections.ObjectModel;
using System.Windows.Input;
using MmaManager.Core.Model;
using MmaManager.Wpf.Mvvm;

namespace MmaManager.Wpf.ViewModels;

/// <summary>One message in the inbox list.</summary>
public sealed class MailRow : ObservableObject
{
    public MailRow(MailMessage m, World w)
    {
        Model = m;
        When = m.Date.ToString("d MMM");
        From = m.From;
        Subject = m.Subject;
        Category = FighterRow.Pretty(m.Category.ToString());
        _unread = !m.Read;

        // The message's deep links, when the sim stamped who/what it's about.
        if (m.AboutFighter is { } fid && w.Get(fid) is { } f)
        {
            FighterLinkId = fid.Value;
            FighterLinkLabel = $"👤 Open {f.FullName}";
        }
        if (m.AboutEvent is { } eid && w.Events.ContainsKey(eid))
        {
            EventLinkId = eid.Value;
            EventLinkLabel = "🎫 Open the event page";
        }
    }
    public MailMessage Model { get; }
    public string When { get; }
    public string From { get; }
    public string Subject { get; }
    public string Category { get; }

    public int FighterLinkId { get; }
    public string FighterLinkLabel { get; } = "";
    public bool HasFighterLink => FighterLinkId != 0;
    public int EventLinkId { get; }
    public string EventLinkLabel { get; } = "";
    public bool HasEventLink => EventLinkId != 0;

    private bool _unread;
    public bool Unread { get => _unread; set => Set(ref _unread, value); }
}

/// <summary>The player's inbox: a message list, a reading pane, and an unread count.</summary>
public sealed class MailViewModel : ObservableObject
{
    private World? _world;

    public MailViewModel()
    {
        MarkAllReadCommand = new RelayCommand(MarkAllRead, () => UnreadCount > 0);
    }

    public ICommand MarkAllReadCommand { get; }

    private void MarkAllRead()
    {
        if (_world is null) return;
        foreach (var m in _world.Mail) m.Read = true;
        foreach (var r in Messages) r.Unread = false;
        UnreadCount = 0;
    }

    public ObservableCollection<MailRow> Messages { get; } = new();

    private int _unreadCount;
    public int UnreadCount { get => _unreadCount; private set { if (Set(ref _unreadCount, value)) OnPropertyChanged(nameof(Header)); } }
    public string Header => UnreadCount > 0 ? $"Inbox ({UnreadCount} unread)" : "Inbox";

    private MailRow? _selected;
    public MailRow? Selected
    {
        get => _selected;
        set { if (Set(ref _selected, value)) { Open(value); OnPropertyChanged(nameof(HasSelection)); } }
    }
    public bool HasSelection => _selected is not null;

    private string _readSubject = "Select a message.";
    public string ReadSubject { get => _readSubject; private set => Set(ref _readSubject, value); }

    private string _readMeta = "";
    public string ReadMeta { get => _readMeta; private set => Set(ref _readMeta, value); }

    private string _readBody = "";
    public string ReadBody { get => _readBody; private set => Set(ref _readBody, value); }

    public void Attach(World world)
    {
        _world = world;
        Messages.Clear();
        foreach (var m in Enumerable.Reverse(world.Mail).Take(300))
            Messages.Add(new MailRow(m, world));
        UnreadCount = world.Mail.Count(m => !m.Read);
        ReadSubject = "Select a message.";
        ReadMeta = "";
        ReadBody = "";
    }

    private void Open(MailRow? row)
    {
        if (row is null) return;
        ReadSubject = row.Subject;
        ReadMeta = $"{row.From} · {row.Model.Date:ddd d MMM yyyy}";
        ReadBody = row.Model.Body;
        if (!row.Model.Read)
        {
            row.Model.Read = true;
            row.Unread = false;
            if (_world is not null) UnreadCount = _world.Mail.Count(m => !m.Read);
        }
    }
}
