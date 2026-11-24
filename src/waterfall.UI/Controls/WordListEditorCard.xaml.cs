using System.Collections;
using System.Windows.Input;

namespace waterfall.UI.Controls;

public partial class WordListEditorCard : ContentView
{
    public static readonly BindableProperty TitleProperty =
        BindableProperty.Create(nameof(Title), typeof(string), typeof(WordListEditorCard), string.Empty);

    public static readonly BindableProperty PlaceholderProperty =
        BindableProperty.Create(nameof(Placeholder), typeof(string), typeof(WordListEditorCard), string.Empty);

    public static readonly BindableProperty IsDangerProperty =
        BindableProperty.Create(nameof(IsDanger), typeof(bool), typeof(WordListEditorCard), false);

    public static readonly BindableProperty ShowMatchModeProperty =
        BindableProperty.Create(nameof(ShowMatchMode), typeof(bool), typeof(WordListEditorCard), false);

    public static readonly BindableProperty MatchModeTextProperty =
        BindableProperty.Create(nameof(MatchModeText), typeof(string), typeof(WordListEditorCard), string.Empty);

    public static readonly BindableProperty ToggleMatchModeCommandProperty =
        BindableProperty.Create(nameof(ToggleMatchModeCommand), typeof(ICommand), typeof(WordListEditorCard));

    public static readonly BindableProperty WordsProperty =
        BindableProperty.Create(nameof(Words), typeof(IEnumerable), typeof(WordListEditorCard));

    public static readonly BindableProperty HasWordsProperty =
        BindableProperty.Create(nameof(HasWords), typeof(bool), typeof(WordListEditorCard), false);

    public static readonly BindableProperty NewWordTextProperty =
        BindableProperty.Create(nameof(NewWordText), typeof(string), typeof(WordListEditorCard), string.Empty, BindingMode.TwoWay);

    public static readonly BindableProperty AddWordCommandProperty =
        BindableProperty.Create(nameof(AddWordCommand), typeof(ICommand), typeof(WordListEditorCard));

    public static readonly BindableProperty RemoveWordCommandProperty =
        BindableProperty.Create(nameof(RemoveWordCommand), typeof(ICommand), typeof(WordListEditorCard));

    public static readonly BindableProperty ClearWordsCommandProperty =
        BindableProperty.Create(nameof(ClearWordsCommand), typeof(ICommand), typeof(WordListEditorCard));

    public WordListEditorCard()
    {
        InitializeComponent();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public bool IsDanger
    {
        get => (bool)GetValue(IsDangerProperty);
        set => SetValue(IsDangerProperty, value);
    }

    public bool ShowMatchMode
    {
        get => (bool)GetValue(ShowMatchModeProperty);
        set => SetValue(ShowMatchModeProperty, value);
    }

    public string MatchModeText
    {
        get => (string)GetValue(MatchModeTextProperty);
        set => SetValue(MatchModeTextProperty, value);
    }

    public ICommand? ToggleMatchModeCommand
    {
        get => (ICommand?)GetValue(ToggleMatchModeCommandProperty);
        set => SetValue(ToggleMatchModeCommandProperty, value);
    }

    public IEnumerable? Words
    {
        get => (IEnumerable?)GetValue(WordsProperty);
        set => SetValue(WordsProperty, value);
    }

    public bool HasWords
    {
        get => (bool)GetValue(HasWordsProperty);
        set => SetValue(HasWordsProperty, value);
    }

    public string NewWordText
    {
        get => (string)GetValue(NewWordTextProperty);
        set => SetValue(NewWordTextProperty, value);
    }

    public ICommand? AddWordCommand
    {
        get => (ICommand?)GetValue(AddWordCommandProperty);
        set => SetValue(AddWordCommandProperty, value);
    }

    public ICommand? RemoveWordCommand
    {
        get => (ICommand?)GetValue(RemoveWordCommandProperty);
        set => SetValue(RemoveWordCommandProperty, value);
    }

    public ICommand? ClearWordsCommand
    {
        get => (ICommand?)GetValue(ClearWordsCommandProperty);
        set => SetValue(ClearWordsCommandProperty, value);
    }
}
