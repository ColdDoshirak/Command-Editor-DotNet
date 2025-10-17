using CommandEditor.Core.Models;

namespace CommandEditor.App.ViewModels;

public class CommandItemViewModel : ObservableObject
{
    private readonly CommandItem _model;

    public CommandItemViewModel(CommandItem model)
    {
        _model = model;
    }

    public CommandItem Model => _model;

    public string Command
    {
        get => _model.Command;
        set
        {
            if (_model.Command != value)
            {
                _model.Command = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(NormalizedKey));
            }
        }
    }

    public CommandPermission Permission
    {
        get => _model.Permission;
        set
        {
            if (_model.Permission != value)
            {
                _model.Permission = value;
                OnPropertyChanged();
            }
        }
    }

    public string Info
    {
        get => _model.Info;
        set
        {
            if (_model.Info != value)
            {
                _model.Info = value;
                OnPropertyChanged();
            }
        }
    }

    public string Group
    {
        get => _model.Group;
        set
        {
            if (_model.Group != value)
            {
                _model.Group = value;
                OnPropertyChanged();
            }
        }
    }

    public string Response
    {
        get => _model.Response;
        set
        {
            if (_model.Response != value)
            {
                _model.Response = value;
                OnPropertyChanged();
            }
        }
    }

    public int Cooldown
    {
        get => _model.Cooldown;
        set
        {
            if (_model.Cooldown != value)
            {
                _model.Cooldown = value;
                OnPropertyChanged();
            }
        }
    }

    public int UserCooldown
    {
        get => _model.UserCooldown;
        set
        {
            if (_model.UserCooldown != value)
            {
                _model.UserCooldown = value;
                OnPropertyChanged();
            }
        }
    }

    public decimal Cost
    {
        get => _model.Cost;
        set
        {
            if (_model.Cost != value)
            {
                _model.Cost = value;
                OnPropertyChanged();
            }
        }
    }

    public int Count
    {
        get => _model.Count;
        set
        {
            if (_model.Count != value)
            {
                _model.Count = value;
                OnPropertyChanged();
            }
        }
    }

    public CommandUsage Usage
    {
        get => _model.Usage;
        set
        {
            if (_model.Usage != value)
            {
                _model.Usage = value;
                OnPropertyChanged();
            }
        }
    }

    public bool Enabled
    {
        get => _model.Enabled;
        set
        {
            if (_model.Enabled != value)
            {
                _model.Enabled = value;
                OnPropertyChanged();
            }
        }
    }

    public string SoundFile
    {
        get => _model.SoundFile;
        set
        {
            if (_model.SoundFile != value)
            {
                _model.SoundFile = value;
                OnPropertyChanged();
            }
        }
    }

    public string? FkSoundFile
    {
        get => _model.FkSoundFile;
        set
        {
            if (_model.FkSoundFile != value)
            {
                _model.FkSoundFile = value;
                OnPropertyChanged();
            }
        }
    }

    public int Volume
    {
        get => _model.Volume;
        set
        {
            if (_model.Volume != value)
            {
                _model.Volume = value;
                OnPropertyChanged();
            }
        }
    }

    public string NormalizedKey => _model.NormalizedKey;
}
