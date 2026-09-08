namespace SoftwareManagement.Application.Analytics;

/// <summary>
/// Counts public page views, and nothing else.
///
/// There is no cookie, no identifier and no third-party script (A-27). The company gets to know
/// which pages people read; it does not get to know who read them, and it therefore owes no consent
/// banner for this.
/// </summary>
public interface IPageViewCounter
{
    /// <summary>
    /// Records one view of a public path. <paramref name="isNewArrival"/> is true when the request
    /// did not come from a link on this site, which is the closest thing to a new visitor that can
    /// be counted without tracking anyone.
    /// </summary>
    Task RecordViewAsync(string path, bool isNewArrival, CancellationToken cancellationToken);

    Task RecordFormStartAsync(string path, CancellationToken cancellationToken);

    Task RecordFormSubmitAsync(string path, CancellationToken cancellationToken);
}
