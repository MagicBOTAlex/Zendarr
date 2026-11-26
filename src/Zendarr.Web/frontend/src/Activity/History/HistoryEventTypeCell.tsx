import React from 'react';
import Icon from 'Components/Icon';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import { icons, kinds } from 'Helpers/Props';
import {
  EpisodeFileDeletedHistory,
  GrabbedHistoryData,
  HistoryData,
  HistoryEventType,
} from 'typings/History';
import translate from 'Utilities/String/translate';

export type DisplayHistoryEventType =
  | HistoryEventType
  | 'movieFileDeleted'
  | 'movieFileRenamed'
  | 'movieFolderImported';

export function getIconName(
  eventType: DisplayHistoryEventType,
  data: HistoryData
) {
  switch (eventType) {
    case 'grabbed':
      return icons.DOWNLOADING;
    case 'seriesFolderImported':
    case 'movieFolderImported':
      return icons.DRIVE;
    case 'downloadFolderImported':
      return icons.DOWNLOADED;
    case 'downloadFailed':
      return icons.DOWNLOADING;
    case 'episodeFileDeleted':
    case 'movieFileDeleted':
      return (data as EpisodeFileDeletedHistory).reason === 'MissingFromDisk'
        ? icons.FILE_MISSING
        : icons.DELETE;
    case 'episodeFileRenamed':
    case 'movieFileRenamed':
      return icons.ORGANIZE;
    case 'downloadIgnored':
      return icons.IGNORE;
    default:
      return icons.UNKNOWN;
  }
}

export function getIconKind(eventType: DisplayHistoryEventType) {
  switch (eventType) {
    case 'downloadFailed':
      return kinds.DANGER;
    default:
      return kinds.DEFAULT;
  }
}

export function getTooltip(
  eventType: DisplayHistoryEventType,
  data: HistoryData
) {
  switch (eventType) {
    case 'grabbed':
      return translate('EpisodeGrabbedTooltip', {
        indexer: (data as GrabbedHistoryData).indexer,
        downloadClient: (data as GrabbedHistoryData).downloadClient,
      });
    case 'seriesFolderImported':
      return translate('SeriesFolderImportedTooltip');
    case 'movieFolderImported':
      return translate('Imported');
    case 'downloadFolderImported':
      return translate('EpisodeImportedTooltip');
    case 'downloadFailed':
      return translate('DownloadFailedEpisodeTooltip');
    case 'episodeFileDeleted':
      return (data as EpisodeFileDeletedHistory).reason === 'MissingFromDisk'
        ? translate('EpisodeFileMissingTooltip')
        : translate('EpisodeFileDeletedTooltip');
    case 'movieFileDeleted':
      return translate('FileDeleted');
    case 'episodeFileRenamed':
      return translate('EpisodeFileRenamedTooltip');
    case 'movieFileRenamed':
      return translate('Rename');
    case 'downloadIgnored':
      return translate('DownloadIgnoredEpisodeTooltip');
    default:
      return translate('UnknownEventTooltip');
  }
}

interface HistoryEventTypeCellProps {
  eventType: DisplayHistoryEventType;
  data: HistoryData;
}

function HistoryEventTypeCell({ eventType, data }: HistoryEventTypeCellProps) {
  const iconName = getIconName(eventType, data);
  const iconKind = getIconKind(eventType);
  const tooltip = getTooltip(eventType, data);

  return (
    <TableRowCell className="w-[35px] text-center" title={tooltip}>
      <Icon name={iconName} kind={iconKind} />
    </TableRowCell>
  );
}

export default HistoryEventTypeCell;
