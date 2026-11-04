using System.Text.Json.Serialization;

namespace Loadout.Core.Updates;

/// <summary>Where an update has got to.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<UpdateStage>))]
public enum UpdateStage
{
    Checking,
    Downloading,
    Verifying,
    Extracting,
    Installing,
    Done,
}

/// <summary>
/// One step of an update, for a screen to show while it waits.
/// </summary>
/// <remarks>
/// The updater reports these and decides nothing from them, so a caller that
/// passes no reporter gets exactly the update it always got. <c>loadout update
/// --progress</c> writes each as one JSON line, which is how the launcher's
/// About window follows an update it started as a child process.
/// </remarks>
/// <param name="Stage">The step.</param>
/// <param name="Bytes">How much has been downloaded, while downloading.</param>
/// <param name="Total">How much there is to download, when the source said.</param>
/// <param name="Message">One line a person can read.</param>
public sealed record UpdateProgress(UpdateStage Stage, long? Bytes, long? Total, string Message);
