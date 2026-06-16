using System.Runtime.ExceptionServices;
using System.Windows;
using LabelMinusinWPF.Common;
using Xunit;

namespace LabelMinusinWPF.Tests;

public class UndoRedoTests
{
    [Fact]
    public void ConsecutiveTextChangesUndoAsSingleStep() => RunInSta(() =>
    {
        var image = new OneImage();
        image.AddLabel(new Point(0.2, 0.3));
        var label = Assert.IsType<OneLabel>(image.SelectedLabel);

        label.Text = "a";
        label.Text = "abc";
        image.Undo();

        Assert.Equal("", label.Text);
        Assert.Contains(label, image.Labels);

        image.Redo();
        Assert.Equal("abc", label.Text);

        image.Undo();
        image.Undo();
        Assert.Empty(image.Labels);
    });

    [Fact]
    public void PositionChangeCanBeUndoneAndRedone() => RunInSta(() =>
    {
        var initialPosition = new Point(0.2, 0.3);
        var movedPosition = new Point(0.7, 0.8);
        var image = new OneImage();
        image.AddLabel(initialPosition);
        var label = Assert.IsType<OneLabel>(image.SelectedLabel);

        label.Position = movedPosition;
        image.Undo();
        Assert.Equal(initialPosition, label.Position);

        image.Redo();
        Assert.Equal(movedPosition, label.Position);
    });

    [Fact]
    public void PendingEditParticipatesInUnsavedDetectionAndSaveBaseline() => RunInSta(() =>
    {
        var image = new OneImage();
        image.AddLabel(new Point(0.2, 0.3));
        image.MarkAsSaved();
        var project = new OneProject();
        project.ImageList.Add(image);
        var label = Assert.IsType<OneLabel>(image.SelectedLabel);

        label.Text = "pending";

        Assert.True(image.HasUnsavedChanges);
        Assert.True(project.HasUnsavedChanges());

        image.MarkAsSaved();

        Assert.False(image.HasUnsavedChanges);
        Assert.False(project.HasUnsavedChanges());
    });

    [Fact]
    public void BranchEditWithSameStackDepthRemainsUnsaved() => RunInSta(() =>
    {
        var image = new OneImage();
        image.AddLabel(new Point(0.2, 0.3));
        var label = Assert.IsType<OneLabel>(image.SelectedLabel);
        label.Text = "saved";
        image.MarkAsSaved();

        image.Undo();
        Assert.Equal("", label.Text);

        label.Text = "branch";
        image.SelectedLabel = null;

        Assert.True(image.HasUnsavedChanges);
        Assert.False(image.RedoCommand.CanExecute(null));

        image.SelectedLabel = label;
        image.Undo();
        Assert.Equal("", label.Text);

        image.Redo();
        Assert.Equal("branch", label.Text);
    });

    [Fact]
    public void DirectEditAfterUndoDisablesAndClearsOldRedoBranch() => RunInSta(() =>
    {
        var image = new OneImage();
        image.AddLabel(new Point(0.2, 0.3));
        var label = Assert.IsType<OneLabel>(image.SelectedLabel);
        label.Text = "first";
        image.SelectedLabel = null;
        image.SelectedLabel = label;
        image.Undo();

        Assert.True(image.RedoCommand.CanExecute(null));

        label.Text = "branch";

        Assert.False(image.RedoCommand.CanExecute(null));

        image.SelectedLabel = null;

        Assert.False(image.RedoCommand.CanExecute(null));
        image.SelectedLabel = label;
        image.Undo();
        Assert.Equal("", label.Text);
    });

    [Fact]
    public void DeletingNewEmptyLabelCancelsItsAddHistory() => RunInSta(() =>
    {
        var image = new OneImage();
        image.AddLabel(new Point(0.2, 0.3));
        var label = Assert.IsType<OneLabel>(image.SelectedLabel);

        image.DeleteLabel(label);

        Assert.Empty(image.Labels);
        Assert.False(image.UndoCommand.CanExecute(null));
        Assert.False(image.RedoCommand.CanExecute(null));
        Assert.False(image.HasUnsavedChanges);
    });

    [Fact]
    public void DeletingSavedEmptyLabelRemainsUndoable() => RunInSta(() =>
    {
        var image = new OneImage();
        image.AddLabel(new Point(0.2, 0.3));
        var label = Assert.IsType<OneLabel>(image.SelectedLabel);
        image.MarkAsSaved();

        image.DeleteLabel(label);

        Assert.True(label.IsDeleted);
        Assert.True(image.UndoCommand.CanExecute(null));

        image.Undo();
        Assert.False(label.IsDeleted);
        Assert.Contains(label, image.Labels);
    });

    [Fact]
    public void ExistingLabelDeleteUsesSoftDeleteAndSupportsUndoRedo() => RunInSta(() =>
    {
        var image = new OneImage();
        var label = new OneLabel("existing", GroupConstants.InBox, new Point(0.2, 0.3));
        image.Labels.Add(label);
        image.SelectedLabel = label;
        image.MarkAsSaved();

        image.DeleteLabel(label);
        Assert.True(label.IsDeleted);
        Assert.Contains(label, image.Labels);

        image.Undo();
        Assert.False(label.IsDeleted);
        Assert.Contains(label, image.Labels);

        image.Redo();
        Assert.True(label.IsDeleted);
        Assert.Contains(label, image.Labels);
    });

    [Fact]
    public void HistoricalBulkAddsRemainIndividuallyUndoable() => RunInSta(() =>
    {
        var image = new OneImage();
        image.AddLabelWithHistory(new OneLabel("one", GroupConstants.InBox, new Point(0.2, 0.3)));
        image.AddLabelWithHistory(new OneLabel("two", GroupConstants.InBox, new Point(0.4, 0.5)));

        Assert.Equal(2, image.Labels.Count);

        image.Undo();
        Assert.Single(image.Labels);
        image.Undo();
        Assert.Empty(image.Labels);

        image.Redo();
        Assert.Single(image.Labels);
        image.Redo();
        Assert.Equal(2, image.Labels.Count);
    });

    [Fact]
    public void UndoToSavePointClearsUnsavedChanges() => RunInSta(() =>
    {
        var image = new OneImage();
        image.AddLabel(new Point(0.2, 0.3));
        var label = Assert.IsType<OneLabel>(image.SelectedLabel);
        label.Text = "saved";
        image.MarkAsSaved();

        label.Text = "edited";
        image.Undo();

        Assert.Equal("saved", label.Text);
        Assert.False(image.HasUnsavedChanges);
    });

    [Fact]
    public void EditingAfterUndoToSavePointKeepsSaveBaselineDirty() => RunInSta(() =>
    {
        var image = new OneImage();
        image.AddLabel(new Point(0.2, 0.3));
        var label = Assert.IsType<OneLabel>(image.SelectedLabel);
        label.Text = "saved";
        image.MarkAsSaved();

        label.Text = "edited";
        image.Undo();
        Assert.False(image.HasUnsavedChanges);

        label.Text = "branch";
        image.SelectedLabel = null;

        Assert.Equal("branch", label.Text);
        Assert.True(image.HasUnsavedChanges);
        Assert.False(image.RedoCommand.CanExecute(null));
    });

    [Fact]
    public void EditingBeforeSavedBranchDiscardsSaveBaseline() => RunInSta(() =>
    {
        var image = new OneImage();
        image.AddLabel(new Point(0.2, 0.3));
        var label = Assert.IsType<OneLabel>(image.SelectedLabel);
        label.Text = "saved";
        image.MarkAsSaved();

        image.Undo();
        image.Undo();
        image.AddLabel(new Point(0.4, 0.5));

        Assert.Single(image.Labels);
        Assert.True(image.HasUnsavedChanges);
        Assert.False(image.RedoCommand.CanExecute(null));
    });

    [Fact]
    public void CancelLatestAfterRedoBranchDiscardDoesNotCorruptSaveState() => RunInSta(() =>
    {
        var image = new OneImage();
        image.AddLabel(new Point(0.2, 0.3));
        var savedLabel = Assert.IsType<OneLabel>(image.SelectedLabel);
        savedLabel.Text = "saved";
        image.MarkAsSaved();

        image.AddLabel(new Point(0.4, 0.5));
        image.Undo();
        image.AddLabel(new Point(0.6, 0.7));
        var newLabel = Assert.IsType<OneLabel>(image.SelectedLabel);

        image.DeleteLabel(newLabel);

        Assert.Single(image.Labels);
        Assert.Same(savedLabel, image.Labels[0]);
        Assert.False(image.HasUnsavedChanges);
        Assert.False(image.RedoCommand.CanExecute(null));
    });

    [Fact]
    public void SavedEmptyLabelDeleteDoesNotCancelLatestAfterCursorRewrite() => RunInSta(() =>
    {
        var image = new OneImage();
        image.AddLabel(new Point(0.2, 0.3));
        var savedLabel = Assert.IsType<OneLabel>(image.SelectedLabel);
        image.MarkAsSaved();

        image.AddLabel(new Point(0.4, 0.5));
        image.Undo();
        image.SelectedLabel = savedLabel;
        image.DeleteLabel(savedLabel);

        Assert.True(savedLabel.IsDeleted);
        Assert.Contains(savedLabel, image.Labels);
        Assert.True(image.HasUnsavedChanges);
        Assert.False(image.RedoCommand.CanExecute(null));

        image.Undo();
        Assert.False(savedLabel.IsDeleted);
        Assert.False(image.HasUnsavedChanges);
    });

    private static void RunInSta(Action test)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                test();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (error != null)
            ExceptionDispatchInfo.Capture(error).Throw();
    }
}
