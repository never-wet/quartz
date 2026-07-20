using System.Windows;

namespace Quartz;

public partial class PermissionPromptWindow : Window
{
    internal PermissionPromptWindow(string domain, string permissionName, string origin)
    {
        InitializeComponent();
        RequestText.Text = $"{domain} wants to use your {permissionName}.";
        OriginText.Text = origin;
    }

    private void AllowButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void BlockButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
