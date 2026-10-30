# SettingRow

A value a command keeps, shown as a setting rather than a form. It is used for a project's style, a project's context values, voice time settings, where calls for help go, and the webhook.

## What it shows

- **The label** and a line on what it changes.
- **What it is now**, at the right ("now house"), or what unset means ("nowhere: calls for help are off").
- **The control:** a text box, a number, a choice, or a Switch for on and off.
- **Set it** (or a name for what it does, such as "Use it"). It is disabled until the value differs, and says why.
- **Clear it**, when the command can unset it.
- **The line** that sets or clears it, updated as you change it.

A Switch acts at once, because on and off need no second press. Its line shows what the next press will run.

## Held

`disabledReason` holds the row: the control is disabled, Set it and Clear it are disabled and described by the reason, and the reason stands in place of the line, as a disabled HaloButton's does. Like any reason it shows whatever the commands setting, since it is not a command. Use it when the row can't change anything here, such as while Studio is only watching.

## Do and don't

- Do show the current value read from the command, never a guess.
- Don't use it for something that is only ever added and removed. That's an ItemList.
