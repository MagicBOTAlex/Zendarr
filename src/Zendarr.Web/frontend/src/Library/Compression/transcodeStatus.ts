import { IconName } from 'Components/Icon';
import { icons } from 'Helpers/Props';
import translate from 'Utilities/String/translate';

const STATUS_LABEL_KEYS: Record<string, string> = {
  Queued: 'Queued',
  Running: 'TranscodeStatusRunning',
  AwaitingReview: 'CompressionReview',
  Transferring: 'Transferring',
  Completed: 'Completed',
  Failed: 'Failed',
  Cancelled: 'TranscodeStatusCancelled',
  Skipped: 'TranscodeStatusSkipped',
};

const RATE_CONTROL_LABEL_KEYS: Record<string, string> = {
  'constant-quality': 'TranscodeRateControlConstantQuality',
  vbr: 'TranscodeRateControlVbr',
};

export type TranscodeTone =
  | 'neutral'
  | 'info'
  | 'primary'
  | 'success'
  | 'warning'
  | 'danger'
  | 'purple';

// Status colors are theme tokens. The light theme uses the darker alert text
// colors so a badge stays legible on its tinted background; the dark theme
// uses the vivid status colors. `light-dark()` keeps both in one class.
export const TRANSCODE_TONE_BG_CLASSES: Record<TranscodeTone, string> = {
  neutral: 'bg-[var(--pageFooterBackground)]',
  info: 'bg-[color-mix(in_srgb,var(--infoColor)_16%,transparent)]',
  primary: 'bg-[color-mix(in_srgb,var(--themeBlue)_16%,transparent)]',
  success: 'bg-[color-mix(in_srgb,var(--successColor)_16%,transparent)]',
  warning: 'bg-[color-mix(in_srgb,var(--warningColor)_18%,transparent)]',
  danger: 'bg-[color-mix(in_srgb,var(--dangerColor)_16%,transparent)]',
  purple: 'bg-[color-mix(in_srgb,var(--purple)_16%,transparent)]',
};

export const TRANSCODE_TONE_TEXT_CLASSES: Record<TranscodeTone, string> = {
  neutral: 'text-[var(--textColor)]',
  info: 'text-[light-dark(var(--alertInfoColor),color-mix(in_srgb,var(--infoColor)_80%,white))]',
  primary: 'text-[light-dark(var(--alertInfoColor),var(--themeBlue))]',
  success:
    'text-[light-dark(var(--alertSuccessColor),color-mix(in_srgb,var(--successColor)_55%,white))]',
  warning: 'text-[light-dark(var(--alertWarningColor),var(--warningColor))]',
  danger:
    'text-[light-dark(var(--alertDangerColor),color-mix(in_srgb,var(--dangerColor)_80%,white))]',
  purple:
    'text-[light-dark(var(--purple),color-mix(in_srgb,var(--purple)_55%,white))]',
};

const STATUS_TONES: Record<string, TranscodeTone> = {
  Queued: 'neutral',
  Running: 'primary',
  AwaitingReview: 'warning',
  Transferring: 'purple',
  Completed: 'success',
  Failed: 'danger',
  Cancelled: 'neutral',
  Skipped: 'warning',
};

const STATUS_ICONS: Record<string, IconName> = {
  Queued: icons.PENDING,
  Running: icons.ACTIONS,
  AwaitingReview: icons.VIEW,
  Transferring: icons.EXPORT,
  Completed: icons.CHECK_CIRCLE,
  Failed: icons.DANGER,
  Cancelled: icons.REMOVE,
  Skipped: icons.INFO,
};

export function getTranscodeStatusLabel(status: string) {
  const key = STATUS_LABEL_KEYS[status];

  return key ? translate(key) : status;
}

export function getTranscodeStatusTone(status: string) {
  return STATUS_TONES[status] ?? 'neutral';
}

export function getTranscodeStatusIcon(status: string) {
  return STATUS_ICONS[status] ?? icons.INFO;
}

export function getRateControlLabel(rateControl?: string) {
  if (!rateControl) {
    return '';
  }

  const key = RATE_CONTROL_LABEL_KEYS[rateControl];

  return key ? translate(key) : rateControl;
}
