namespace Core.Interface
{
    /// <summary>
    /// Interface for device worker service to enable event-driven updates
    /// </summary>
    public interface IDeviceWorkerService
    {
        /// <summary>
        /// Refresh a device polling task (for create/update operations)
        /// </summary>
        Task RefreshDeviceAsync(Guid deviceId);

        /// <summary>
        /// Remove a device polling task (for delete/disable operations)
        /// </summary>
        Task RemoveDeviceAsync(Guid deviceId);

        /// <summary>
        /// Refresh a storage flow task (for create/update operations)
        /// </summary>
        Task RefreshStorageFlowAsync(Guid storageFlowId);

        /// <summary>
        /// Remove a storage flow task (for delete/disable operations)
        /// </summary>
        Task RemoveStorageFlowAsync(Guid storageFlowId);

        /// <summary>
        /// Refresh all devices and storage flows
        /// </summary>
        Task RefreshAllAsync();
    }
}
