using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Windows.Storage.Pickers;
using XrealScreen.App.ViewModels;
using XrealScreen.Core.Hud;

namespace XrealScreen.App.Pages;

public sealed partial class HudPage : Page
{
    private readonly Dictionary<int, WriteableBitmap> _bitmaps = [];
    private readonly Dictionary<int, Border> _tiles = [];
    private int _dragId = -1;
    private double _grabX;
    private double _grabY;

    public HudPage()
    {
        InitializeComponent();
        Hud.ImagesChanged += (_, _) => RebuildBoard();
        Loaded += (_, _) => RebuildBoard();
        ImageBoard.SizeChanged += (_, _) => RebuildBoard();
    }

    public HudViewModel Hud => App.Hud;

    private async void AddImages_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker(App.MainWindow.AppWindow.Id);
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".bmp");
            picker.FileTypeFilter.Add(".gif");
            picker.FileTypeFilter.Add(".tif");
            picker.FileTypeFilter.Add(".tiff");
            picker.FileTypeFilter.Add(".webp");
            IReadOnlyList<PickFileResult> files = await picker.PickMultipleFilesAsync();
            if (files.Count == 0)
            {
                return;
            }

            var paths = new string[files.Count];
            for (int i = 0; i < files.Count; i++)
            {
                paths[i] = files[i].Path;
            }

            Hud.AddImageFiles(paths);
        }
        catch (Exception ex)
        {
            CrashLog.Write("HudImages", ex);
        }
    }

    private void RemoveImage_Click(object sender, RoutedEventArgs e) => Hud.RemoveSelectedImage();

    private void Board_Pressed(object sender, PointerRoutedEventArgs e)
    {
        if (ImageBoard.ActualWidth < 2 || ImageBoard.ActualHeight < 2)
        {
            return;
        }

        Windows.Foundation.Point point = e.GetCurrentPoint(ImageBoard).Position;
        int id = Hit(point.X, point.Y);
        if (id < 0)
        {
            return;
        }

        Hud.SelectImage(id);
        HudImageItem? item = Find(id);
        if (item is null)
        {
            return;
        }

        (double left, double top, double width, double height) = TileRect(item);
        _dragId = id;
        _grabX = point.X - (left + (width / 2));
        _grabY = point.Y - (top + (height / 2));
        ImageBoard.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void Board_Moved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragId < 0 || ImageBoard.ActualWidth < 2 || ImageBoard.ActualHeight < 2)
        {
            return;
        }

        Windows.Foundation.Point point = e.GetCurrentPoint(ImageBoard).Position;
        float ndcX = (float)(((point.X - _grabX) / ImageBoard.ActualWidth * 2) - 1);
        float ndcY = (float)(1 - ((point.Y - _grabY) / ImageBoard.ActualHeight * 2));
        Hud.MoveImage(_dragId, ndcX, ndcY, save: false);
        if (_tiles.TryGetValue(_dragId, out Border? tile) && Find(_dragId) is { } item)
        {
            (double left, double top, _, _) = TileRect(item);
            Canvas.SetLeft(tile, left);
            Canvas.SetTop(tile, top);
        }

        e.Handled = true;
    }

    private void Board_Released(object sender, PointerRoutedEventArgs e)
    {
        if (_dragId < 0)
        {
            return;
        }

        int id = _dragId;
        _dragId = -1;
        ImageBoard.ReleasePointerCapture(e.Pointer);
        if (Find(id) is { } item)
        {
            Hud.MoveImage(id, item.CenterX, item.CenterY, save: true);
        }
    }

    private void Board_Wheel(object sender, PointerRoutedEventArgs e)
    {
        int delta = e.GetCurrentPoint(ImageBoard).Properties.MouseWheelDelta;
        if (delta == 0)
        {
            return;
        }

        Hud.NudgeSelectedSize(delta > 0 ? 0.06f : -0.06f);
        e.Handled = true;
    }

    private void RebuildBoard()
    {
        if (ImageBoard.ActualWidth < 2 || ImageBoard.ActualHeight < 2)
        {
            return;
        }

        ImageCanvas.Children.Clear();
        _tiles.Clear();
        var live = new HashSet<int>();
        foreach (HudImageItem item in Hud.Images)
        {
            live.Add(item.Id);
            (double left, double top, double width, double height) = TileRect(item);
            var image = new Image
            {
                Source = BitmapFor(item),
                Width = width,
                Height = height,
                Stretch = Stretch.Fill,
                IsHitTestVisible = false,
            };
            var tile = new Border
            {
                Width = width,
                Height = height,
                BorderThickness = new Thickness(item.Id == Hud.SelectedImageId ? 2 : 0),
                BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 120, 220, 255)),
                IsHitTestVisible = false,
                Child = image,
            };
            Canvas.SetLeft(tile, left);
            Canvas.SetTop(tile, top);
            ImageCanvas.Children.Add(tile);
            _tiles[item.Id] = tile;
        }

        foreach (int id in _bitmaps.Keys.Where(id => !live.Contains(id)).ToArray())
        {
            _bitmaps.Remove(id);
        }

        ImageBoardEmpty.Visibility = Hud.Images.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private WriteableBitmap BitmapFor(HudImageItem item)
    {
        if (_bitmaps.TryGetValue(item.Id, out WriteableBitmap? existing))
        {
            return existing;
        }

        var bitmap = new WriteableBitmap(item.Width, item.Height);
        using (Stream stream = bitmap.PixelBuffer.AsStream())
        {
            stream.Write(item.Bgra, 0, item.Bgra.Length);
        }

        _bitmaps[item.Id] = bitmap;
        return bitmap;
    }

    private (double Left, double Top, double Width, double Height) TileRect(HudImageItem item)
    {
        int boardWidth = Math.Max(1, (int)ImageBoard.ActualWidth);
        int boardHeight = Math.Max(1, (int)ImageBoard.ActualHeight);
        (float left, float right, float top, float bottom) = HudPicturePlacement.Place(
            item.Width, item.Height, boardWidth, boardHeight, item.CenterX, item.CenterY, item.Size);
        double x = ((left * 0.5f) + 0.5f) * boardWidth;
        double y = (0.5f - (top * 0.5f)) * boardHeight;
        double width = Math.Max(8, (right - left) * 0.5 * boardWidth);
        double height = Math.Max(8, (top - bottom) * 0.5 * boardHeight);
        return (x, y, width, height);
    }

    private int Hit(double x, double y)
    {
        for (int i = Hud.Images.Count - 1; i >= 0; i--)
        {
            (double left, double top, double width, double height) = TileRect(Hud.Images[i]);
            if (x >= left && y >= top && x < left + width && y < top + height)
            {
                return Hud.Images[i].Id;
            }
        }

        return -1;
    }

    private HudImageItem? Find(int id)
    {
        foreach (HudImageItem item in Hud.Images)
        {
            if (item.Id == id)
            {
                return item;
            }
        }

        return null;
    }
}
