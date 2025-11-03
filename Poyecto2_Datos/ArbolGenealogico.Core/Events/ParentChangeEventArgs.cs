using System;

namespace ArbolGenealogico.Core.Events
{
    public class ParentChangedEventArgs : EventArgs
    {
        public Guid ChildId { get; }
        public Guid? OldParentId { get; }
        public Guid? NewParentId { get; }

        public ParentChangedEventArgs(Guid childId, Guid? oldParentId, Guid? newParentId)
        {
            ChildId = childId;
            OldParentId = oldParentId;
            NewParentId = newParentId;
        }
    }
}
