using Recrovit.RecroGridFramework.Abstraction.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Recrovit.RecroGridFramework.Client.Models;

public class FormViewData
{
    public FormViewData(List<RgfForm.Tab> formTabs, RgfDynamicDictionary dataRec)
    {
        FormTabs = formTabs;
        DataRec = dataRec;
    }


    public RgfDynamicDictionary DataRec { get; set; }

    public List<RgfForm.Tab> FormTabs { get; set; }

    public RgfEntityKey? EntityKey { get; internal set; }

    public string? StyleSheetUrl { get; set; }
}
