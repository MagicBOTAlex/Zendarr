import classNames from 'classnames';
import React from 'react';
import Icon from 'Components/Icon';
import {
  getTranscodeStatusIcon,
  getTranscodeStatusLabel,
  getTranscodeStatusTone,
  TRANSCODE_TONE_BG_CLASSES,
  TRANSCODE_TONE_TEXT_CLASSES,
} from './transcodeStatus';

const BADGE_CLASS =
  'inline-flex shrink-0 items-center gap-[6px] rounded-full px-[9px] py-[3px] text-[11px] font-semibold leading-none whitespace-nowrap';

interface TranscodeStatusBadgeProps {
  status: string;
  className?: string;
}

function TranscodeStatusBadge({
  status,
  className,
}: TranscodeStatusBadgeProps) {
  const tone = getTranscodeStatusTone(status);

  return (
    <span
      className={classNames(
        BADGE_CLASS,
        TRANSCODE_TONE_BG_CLASSES[tone],
        TRANSCODE_TONE_TEXT_CLASSES[tone],
        className
      )}
    >
      <Icon
        name={getTranscodeStatusIcon(status)}
        size={10}
        aria-hidden={true}
      />
      {getTranscodeStatusLabel(status)}
    </span>
  );
}

export default TranscodeStatusBadge;
