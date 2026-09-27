using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;

namespace GymPro.App.Infrastructure;

public static class Dialogs
{
    public static void Error(string message) =>
        MessageBox.Show(Application.Current.MainWindow, message, "Error", MessageBoxButton.OK, MessageBoxImage.Warning);

    public static void Info(string message) =>
        MessageBox.Show(Application.Current.MainWindow, message, "GymPro", MessageBoxButton.OK, MessageBoxImage.Information);

    public static bool Confirm(string message) =>
        MessageBox.Show(Application.Current.MainWindow, message, "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public static string? OpenFile(string filter)
    {
        var dlg = new OpenFileDialog { Filter = filter };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }
}

/// <summary>Shared busy/status handling and friendly messages for the errors users can actually hit.</summary>
public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? Status { get; set; }

    /// <summary>Runs an action with busy state and friendly errors. <paramref name="success"/> is shown as a toast.</summary>
    protected async Task<bool> RunAsync(Func<Task> action, string? success = null)
    {
        if (IsBusy)
        {
            return false;
        }

        IsBusy = true;
        Status = null;
        try
        {
            await action();
            Status = success;
            if (success is not null)
            {
                Toast.Show(success);
            }

            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            Dialogs.Error("Another user changed this record while you were editing it. It has been reloaded; please apply your change again.");
            await OnConcurrencyConflictAsync();
            return false;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or UnauthorizedAccessException or InvalidDataException or FileNotFoundException)
        {
            Dialogs.Error(ex.Message);
            return false;
        }
        catch (DbUpdateException ex)
        {
            Dialogs.Error("The database rejected the change: " + (ex.InnerException?.Message ?? ex.Message));
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    protected virtual Task OnConcurrencyConflictAsync() => Task.CompletedTask;
}

/// <summary>Non-blocking success/info messages shown in the main window (replaces "OK" message boxes).</summary>
public static class Toast
{
    public static event Action<string, GymPro.Core.Rules.StatusTone>? Shown;

    public static void Show(string message, GymPro.Core.Rules.StatusTone tone = GymPro.Core.Rules.StatusTone.Success) =>
        Shown?.Invoke(message, tone);
}

public sealed class BytesToImageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Images.FromBytes(value as byte[], 300);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class UtcToLocalConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DateTime d ? DateTime.SpecifyKind(d, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", culture) : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class NullToVisibleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Visible when a count is 0: drives empty-state messages.</summary>
public sealed class CountToVisibleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>"Ahmed Mohamed" -> "AM"; used for avatars when a member has no photo.</summary>
public sealed class InitialsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var parts = (value as string ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..1].ToUpper(culture),
            _ => (parts[0][..1] + parts[^1][..1]).ToUpper(culture),
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
