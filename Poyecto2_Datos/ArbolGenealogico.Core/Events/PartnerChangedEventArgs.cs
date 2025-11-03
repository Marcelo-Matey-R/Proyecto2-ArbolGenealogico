using System;

namespace ArbolGenealogico.Core.Events
{
    public class PartnerChangedEventArgs : EventArgs
    {
        public Guid PersonId { get; }
        public Guid? OldPartnerId { get; }
        public Guid? NewPartnerId { get; }

        public PartnerChangedEventArgs(Guid personId, Guid? oldPartnerId, Guid? newPartnerId)
        {
            PersonId = personId;
            OldPartnerId = oldPartnerId;
            NewPartnerId = newPartnerId;
        }
    }
}
