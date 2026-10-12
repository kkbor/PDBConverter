using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PDBViewer
{
    public class ChainSequenceItem
    {
        public char ChainId { get; }

        public ObservableCollection<ResidueSequenceItem> Residues { get; }
            = new();

        public ChainSequenceItem(char chainId)
        {
            ChainId = chainId;
        }
    }
}
