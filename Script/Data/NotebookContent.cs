using Godot;

/// <summary>Notebook copy kept as editable data while the page uses icon-only controls.</summary>
[Tool]
[GlobalClass]
public partial class NotebookContent : Resource
{
    [Export] public string Title { get; set; } = "拼接笔记本";
    [Export(PropertyHint.MultilineText)] public string Instructions { get; set; }
        = "从下方素材卡拖出纹样\n放到纸面上进行拼接\n\n原卡保留，可重复取用";
    [Export] public string DeletePrompt { get; set; } = "拖到这里删除纹样碎片";
    [Export] public string DeleteHoverPrompt { get; set; } = "松手删除碎片";
    [Export] public string BackLabel { get; set; } = "返回花园";
    [Export] public string DeleteSelectionLabel { get; set; } = "删除选中碎片";
}
