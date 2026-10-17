// Where Studio's "Report a problem" goes: the Studio bug form, which applies
// the bug and studio labels itself, since GitHub ignores a labels= value in a
// link from anybody without triage rights. The version and platform are
// filled in through the form's field ids; nothing else about the machine is.

export const STUDIO_BUG_FORM = 'https://github.com/ntatschner/loadout-cli/issues/new?template=studio_bug.yml';

export function reportLink(about: { version: string; platform: string } | null | undefined): string {
  if (!about) {
    return STUDIO_BUG_FORM;
  }

  const fields = new URLSearchParams({ version: about.version, platform: about.platform });

  return `${STUDIO_BUG_FORM}&${fields.toString()}`;
}
