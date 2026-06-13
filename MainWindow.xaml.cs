using ihsbmodern.Models;
using ihsbmodern.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using Windows.System;

namespace ihsbmodern;

public sealed partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        AppWindow.SetIcon("Assets/AppIcon.ico");

        _vm = new MainViewModel();
        RootGrid.DataContext = _vm;

        if (!string.IsNullOrEmpty(_vm.ApiKey))
            ApiKeyBox.Password = _vm.ApiKey;

        ModelSelector.SelectedItem = _vm.SelectedModel;
        EffortSelector.SelectedItem = _vm.SelectedReasoningEffort;
        EffortPanel.Visibility = _vm.ShowReasoningEffort ? Visibility.Visible : Visibility.Collapsed;
        UpdateWorkDirLabel();

        _vm.CurrentMessages.CollectionChanged += (_, _) =>
        {
            BuildMessageList();
            ScrollToBottom();
        };
        _vm.PropertyChanged += Vm_PropertyChanged;

        BuildFolderTree();
        BuildMessageList();
        UpdateChatTitle();
    }

    private void Vm_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedChat))
        {
            BuildMessageList();
            UpdateChatTitle();
        }
        else if (e.PropertyName == nameof(MainViewModel.SelectedFolder))
        {
            BuildFolderTree();
        }
        else if (e.PropertyName == nameof(MainViewModel.ChatsInSelectedFolder))
        {
            BuildFolderTree();
        }
        else if (e.PropertyName == nameof(MainViewModel.IsWaitingForResponse))
        {
            LoadingRing.IsActive = _vm.IsWaitingForResponse;
            LoadingRing.Visibility = _vm.IsWaitingForResponse ? Visibility.Visible : Visibility.Collapsed;
            SendButton.IsEnabled = !_vm.IsWaitingForResponse;
        }
        else if (e.PropertyName == nameof(MainViewModel.StatusMessage))
        {
            StatusText.Text = _vm.StatusMessage;
        }
        else if (e.PropertyName == nameof(MainViewModel.ShowReasoningEffort))
        {
            EffortPanel.Visibility = _vm.ShowReasoningEffort ? Visibility.Visible : Visibility.Collapsed;
        }
        else if (e.PropertyName == nameof(MainViewModel.WorkingDirectory))
        {
            UpdateWorkDirLabel();
        }
    }

    private void UpdateWorkDirLabel()
    {
        WorkDirLabel.Text = _vm.WorkingDirectory ?? "No working directory";
    }

    #region Settings

    private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        _vm.ApiKey = ApiKeyBox.Password;
    }

    private void ModelSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox cb && cb.SelectedItem is string model)
            _vm.SelectedModel = model;
    }

    private void EffortSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox cb && cb.SelectedItem is string effort)
            _vm.SelectedReasoningEffort = effort;
    }

    private async void PickFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.SuggestedStartLocation = PickerLocationId.Desktop;
        picker.FileTypeFilter.Add("*");

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        if (folder != null)
        {
            _vm.SetWorkingDirectoryCommand.Execute(folder.Path);
        }
    }

    #endregion

    #region Folder Tree

    private void BuildFolderTree()
    {
        FoldersTree.RootNodes.Clear();

        foreach (var folder in _vm.Folders)
        {
            var folderNode = new TreeViewNode
            {
                Content = folder,
                IsExpanded = true
            };

            var chats = _vm.Chats.Where(c => c.FolderId == folder.Id);
            foreach (var chat in chats)
            {
                folderNode.Children.Add(new TreeViewNode { Content = chat });
            }

            FoldersTree.RootNodes.Add(folderNode);
        }
    }

    private void FoldersTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is TreeViewNode node)
        {
            if (node.Content is Chat chat)
            {
                _vm.SelectedChat = chat;
                _vm.SelectedFolder = _vm.Folders.FirstOrDefault(f => f.Id == chat.FolderId);
            }
            else if (node.Content is Folder folder)
            {
                _vm.SelectedFolder = folder;
            }
        }
    }

    private void FoldersTree_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement fe && fe.DataContext is TreeViewNode node)
        {
            if (node.Content is Folder folder)
            {
                _vm.SelectedFolder = folder;
                FolderContextMenu.ShowAt(fe, e.GetPosition(fe));
            }
            else if (node.Content is Chat chat)
            {
                _vm.SelectedChat = chat;
                ChatContextMenu.ShowAt(fe, e.GetPosition(fe));
            }
        }
    }

    #endregion

    #region Folder Management

    private void AddFolderButton_Click(object sender, RoutedEventArgs e)
    {
        _vm.NewFolderName = NewFolderNameBox.Text;
        _vm.AddFolderCommand.Execute(null);
        NewFolderNameBox.Text = "";
        BuildFolderTree();
    }

    private void NewFolderNameBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            AddFolderButton_Click(sender, e);
            e.Handled = true;
        }
    }

    private void RenameFolderMenu_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedFolder == null) return;
        RenameFolderBox.Text = _vm.SelectedFolder.Name;
        RenameFolderPanel.Visibility = Visibility.Visible;
        RenameFolderBox.Focus(FocusState.Programmatic);
    }

    private void SaveRenameButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedFolder != null && !string.IsNullOrWhiteSpace(RenameFolderBox.Text))
        {
            _vm.SelectedFolder.Name = RenameFolderBox.Text.Trim();
            BuildFolderTree();
        }
        RenameFolderPanel.Visibility = Visibility.Collapsed;
    }

    private void CancelRenameButton_Click(object sender, RoutedEventArgs e)
    {
        RenameFolderPanel.Visibility = Visibility.Collapsed;
    }

    private void RenameFolderBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            SaveRenameButton_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Escape)
        {
            CancelRenameButton_Click(sender, e);
            e.Handled = true;
        }
    }

    private void DeleteFolderMenu_Click(object sender, RoutedEventArgs e)
    {
        _vm.DeleteFolderCommand.Execute(_vm.SelectedFolder);
        BuildFolderTree();
        BuildMessageList();
        UpdateChatTitle();
    }

    #endregion

    #region Chat Management

    private void NewChatButton_Click(object sender, RoutedEventArgs e)
    {
        _vm.NewChatCommand.Execute(null);
        BuildFolderTree();
        BuildMessageList();
        UpdateChatTitle();
    }

    private void DeleteChatMenu_Click(object sender, RoutedEventArgs e)
    {
        _vm.DeleteChatCommand.Execute(_vm.SelectedChat);
        BuildFolderTree();
        BuildMessageList();
        UpdateChatTitle();
    }

    private void UpdateChatTitle()
    {
        ChatTitle.Text = _vm.SelectedChat?.Title ?? "Select or create a chat";
    }

    #endregion

    #region Messages

    private void BuildMessageList()
    {
        MessagesContainer.Children.Clear();

        if (_vm.SelectedChat == null) return;

        foreach (var msg in _vm.CurrentMessages)
        {
            MessagesContainer.Children.Add(CreateMessageBubble(msg));
        }
    }

    private static Border CreateMessageBubble(ChatMessage msg)
    {
        var isUser = msg.IsUser;

        var bgBrush = new SolidColorBrush(isUser
            ? Windows.UI.Color.FromArgb(255, 0, 120, 212)
            : Windows.UI.Color.FromArgb(255, 44, 44, 44));

        var fgBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));

        var stack = new StackPanel { Spacing = 2 };

        if (msg.HasToolActivity)
        {
            var toolPanel = new StackPanel
            {
                Spacing = 2,
                Margin = new Thickness(0, 0, 0, 6),
                Padding = new Thickness(8, 6, 8, 6),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(40, 255, 255, 255)),
            };
            foreach (var activity in msg.ToolActivity)
            {
                toolPanel.Children.Add(new TextBlock
                {
                    Text = $"🔧 {activity}",
                    FontSize = 11,
                    IsTextSelectionEnabled = true,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(200, 200, 220, 255)),
                    FontFamily = new FontFamily("Consolas"),
                });
            }
            stack.Children.Add(new Border
            {
                Child = toolPanel,
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 0, 0, 4),
            });
        }

        var textBlock = new TextBlock
        {
            Text = msg.Content,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            Foreground = fgBrush,
            FontSize = 14,
        };

        var timeBlock = new TextBlock
        {
            Text = msg.Timestamp.ToString("HH:mm"),
            FontSize = 11,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(180, 255, 255, 255)),
            HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 0, 0),
        };

        stack.Children.Add(textBlock);
        stack.Children.Add(timeBlock);

        return new Border
        {
            Child = stack,
            Background = bgBrush,
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 10, 14, 10),
            MaxWidth = 700,
            HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left,
        };
    }

    private void ScrollToBottom()
    {
        MessagesScroller?.UpdateLayout();
        MessagesScroller?.ChangeView(null, MessagesScroller.ScrollableHeight, null, true);
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e)
    {
        _vm.MessageInput = MessageInputBox.Text;
        MessageInputBox.Text = "";
        await _vm.SendMessageCommand.ExecuteAsync(null);
        ScrollToBottom();
    }

    private void MessageInputBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
            if (shift.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
                return;

            SendButton_Click(sender, e);
            e.Handled = true;
        }
    }

    #endregion
}
