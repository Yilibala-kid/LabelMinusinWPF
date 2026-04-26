namespace LabelMinusinWPF.Common;

public static class NavigationHelper
{
    public static int NavigateIndex(int currentIndex, int collectionCount, bool forward)
    {
        if (forward)
            return currentIndex >= 0 && currentIndex < collectionCount - 1 ? currentIndex + 1 : -1;
        else
            return currentIndex > 0 ? currentIndex - 1 : -1;
    }
}
