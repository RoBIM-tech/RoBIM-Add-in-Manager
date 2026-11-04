namespace RoBIM.Manager.Core;

public static class ToolCardAccess
{
    public static bool Visible(bool permitted, bool selected, bool pending) => permitted || selected || pending;
    public static bool RemovalOnly(bool permitted, bool selected) => !permitted && selected;
}
