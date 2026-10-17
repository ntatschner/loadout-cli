// The link must name the template by its file and fill the fields by the ids
// in .github/ISSUE_TEMPLATE/studio_bug.yml. StudioBuildTests reads both files
// and checks they agree, since this side cannot read outside studio/.
import { describe, expect, it } from 'vitest';
import { STUDIO_BUG_FORM, reportLink } from './report';

describe('reportLink', () => {
  it('fills in the version and platform, encoded', () => {
    const link = new URL(reportLink({ version: '0.64.0+abc', platform: 'Microsoft Windows 10.0.26200 x64' }));

    expect(link.searchParams.get('template')).toBe('studio_bug.yml');
    expect(link.searchParams.get('version')).toBe('0.64.0+abc');
    expect(link.searchParams.get('platform')).toBe('Microsoft Windows 10.0.26200 x64');
  });

  it('opens the bare form before the daemon has said what it is', () => {
    expect(reportLink(null)).toBe(STUDIO_BUG_FORM);
  });
});
