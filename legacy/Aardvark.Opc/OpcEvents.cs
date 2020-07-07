using System.Collections.Generic;
using Aardvark.Runtime;
using Aardvark.Base; 
using PH = Aardvark.Opc.PatchHierarchy;

namespace Aardvark.Opc
{
    public static class OpcEvents
    {
        public class OpcMessage
        {
            public string ViewId;
            public string SetId;
            public IEnumerable<PH.PatchHierarchy> Opcs;
        }

        public static EventDescription<Tup<OpcMessage,bool>> AddOpcs
                = new EventDescription<Tup<OpcMessage, bool>>("AddOpcs");

        public static EventDescription<OpcMessage> RemoveOpcs
                = new EventDescription<OpcMessage>("RemoveOpcs");

        /// <summary>
        /// Call back message to react on opc streaming finished. Opc attaches
        /// its id to the event. 
        /// </summary>
        public static EventDescription<string> StreamingFinished
               = new EventDescription<string>("StreamingFinished");
    }
}
