using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BMPC.LegacyEdgeLauncher.Desktop.Services;

/// <summary>
/// Password gate for the settings (gear) menu. The plaintext password is never stored;
/// only a PBKDF2-SHA256 salted hash is embedded, and typed input is verified against it
/// with a constant-time comparison. Prompts on every access attempt.
/// </summary>
public static class SettingsGate
{
    // PBKDF2-SHA256, 100,000 iterations, 32-byte key. Salt and hash are base64.
    // Generated from the configured settings password; the plaintext is intentionally absent.
    private const string SaltBase64 = "Z8VIQdVwLOeH/XFiA21+Kg==";
    private const string HashBase64 = "mQm9u8rF1FLqcD7vBFeS6xhN1XWf6D0pykIvCYDlbBM=";
    private const int Iterations = 100_000;
    private const int KeyLength = 32;

    /// <summary>
    /// Prompts for the settings password and returns true only if it matches.
    /// A blank entry or Cancel returns false without an error dialog.
    /// </summary>
    public static bool Authenticate(Window owner)
    {
        var entered = PromptForPassword(owner);
        if (entered is null)
            return false; // cancelled

        if (Verify(entered))
            return true;

        MessageBox.Show(owner, "Incorrect password.", "Settings locked",
            MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    private static bool Verify(string password)
    {
        var salt = Convert.FromBase64String(SaltBase64);
        var expected = Convert.FromBase64String(HashBase64);
        byte[] actual = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, KeyLength);
        // Constant-time comparison to avoid leaking match length via timing.
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <summary>Shows a modal password dialog. Returns the entered text, or null if cancelled.</summary>
    private static string? PromptForPassword(Window owner)
    {
        var dialog = new Window
        {
            Title = "Settings locked",
            Owner = owner,
            Width = 360,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.ToolWindow,
            Background = Brushes.White,
            Icon = owner.Icon
        };

        var root = new StackPanel { Margin = new Thickness(20) };

        root.Children.Add(new TextBlock
        {
            Text = "Enter the settings password to continue.",
            Foreground = new SolidColorBrush(Color.FromRgb(0x37, 0x41, 0x51)),
            Margin = new Thickness(0, 0, 0, 10),
            TextWrapping = TextWrapping.Wrap
        });

        var box = new PasswordBox { Padding = new Thickness(4, 3, 4, 3), FontSize = 14 };
        root.Children.Add(box);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };

        var ok = new Button
        {
            Content = "Unlock",
            IsDefault = true,
            MinWidth = 90,
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(12, 6, 12, 6),
            Background = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0)
        };
        var cancel = new Button
        {
            Content = "Cancel",
            IsCancel = true,
            MinWidth = 90,
            Padding = new Thickness(12, 6, 12, 6)
        };

        string? result = null;
        ok.Click += (_, _) => { result = box.Password; dialog.DialogResult = true; };

        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);
        dialog.Content = root;

        // Focus the password field when the dialog appears.
        dialog.Loaded += (_, _) => box.Focus();

        return dialog.ShowDialog() == true ? result : null;
    }
}
