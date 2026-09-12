using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace AppleMusicWidget.UI;

/// <summary>
/// Phase 4 skeleton: the 350x150 card flown out upward from TaskbarStrip.
/// Not shown anywhere yet — layout ported from the removed desktop PlayerWidget.
/// </summary>
public partial class PlayerFlyout : Window
{
    private readonly PlayerViewModel _vm;
    private bool _isScrubbing;

    public PlayerFlyout(PlayerViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        _vm.PropertyChanged += OnVmPropertyChanged;
        Loaded += (_, _) =>
        {
            SeekSlider.Value = _vm.ProgressFraction;
            SeekSlider.ApplyTemplate();
            if (SeekSlider.Template.FindName("PART_Track", SeekSlider) is Track track)
            {
                track.Thumb.DragStarted += (_, _) => _isScrubbing = true;
                track.Thumb.DragCompleted += (_, _) =>
                {
                    _vm.SeekTo(SeekSlider.Value);
                    _isScrubbing = false;
                };
            }
        };
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerViewModel.ProgressFraction) && !_isScrubbing)
            SeekSlider.Value = _vm.ProgressFraction;
    }

    private void OnSeekDown(object sender, MouseButtonEventArgs e) => _isScrubbing = true;

    private void OnSeekUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isScrubbing) return;
        _vm.SeekTo(SeekSlider.Value);
        _isScrubbing = false;
    }

    private void OnArtworkSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (ArtworkImage.ActualWidth <= 0 || ArtworkImage.ActualHeight <= 0) return;
        ArtworkImage.Clip = new RectangleGeometry(
            new Rect(0, 0, ArtworkImage.ActualWidth, ArtworkImage.ActualHeight), 8, 8);
    }
}
