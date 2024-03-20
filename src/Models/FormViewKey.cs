using Recrovit.RecroGridFramework.Abstraction.Models;

namespace Recrovit.RecroGridFramework.Client.Models;

public class FormViewKey
{
    public FormViewKey() : this(new RgfEntityKey()) { }

    public FormViewKey(RgfEntityKey entityKey, int rowIndex = -1)
    {
        EntityKey = entityKey;
        RowIndex = rowIndex;
    }

    public RgfEntityKey EntityKey { get; set; }

    public int RowIndex { get; set; }
}