/**
 * Bytes as a person reads them: megabytes under a gigabyte, gigabytes above,
 * to one decimal place where it matters.
 *
 * @param {number | null | undefined} bytes
 */
export function size(bytes) {
  if (bytes === null || bytes === undefined) {
    return 'not measured';
  }

  const megabytes = bytes / (1024 * 1024);

  return megabytes < 1024 ? `${Math.round(megabytes)} MB` : `${(megabytes / 1024).toFixed(1)} GB`;
}
