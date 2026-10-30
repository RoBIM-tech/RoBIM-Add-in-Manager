using System.Drawing.Drawing2D;
namespace RoBIM.Manager;

// Manager owns its editor preview. Host installation and package cache are not prerequisites.
internal static class ManagementButtonIcons
{
    internal static bool IsManagement(string id) => id is "manager:RoBIM.Native.Reload" or "manager:RoBIM.Native.Manager" or "manager:cad-reload" or "manager:cad-manager";
    internal static Bitmap? Create(string id,int size)
    {
        if(!IsManagement(id))return null;
        if(size is not (16 or 32))throw new ArgumentOutOfRangeException(nameof(size));
        var bitmap=new Bitmap(size,size,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics=Graphics.FromImage(bitmap);graphics.SmoothingMode=SmoothingMode.AntiAlias;graphics.Clear(Color.Transparent);graphics.ScaleTransform(size/32f,size/32f);
        using var pen=new Pen(Color.FromArgb(30,91,166),2.5f){StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round};
        if(id.EndsWith("Reload",StringComparison.Ordinal)||id=="manager:cad-reload")
        {
            graphics.DrawArc(pen,6,6,20,20,45,285);
            using var fill=new SolidBrush(pen.Color);graphics.FillPolygon(fill,[new PointF(21,3),new PointF(29,7),new PointF(22,12)]);
        }
        else
        {
            graphics.DrawRectangle(pen,4,5,24,22);graphics.DrawLine(pen,5,11,27,11);
            using var fill=new SolidBrush(Color.FromArgb(77,139,196));graphics.FillRectangle(fill,8,15,5,4);graphics.FillRectangle(fill,8,22,5,2);
            graphics.DrawLine(pen,18,17,24,17);graphics.DrawLine(pen,18,23,24,23);
        }
        return bitmap;
    }
}
