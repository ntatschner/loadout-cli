---
id: framework.django
kind: framework
title: Django
summary: ORM behaviour, migrations and request lifecycle.
globs:
  - '**/manage.py'
  - '**/settings.py'
dependencies:
  - 'Django'
  - 'django'
task_phrases:
  - 'django'
requires:
  - 'language.python'
---

## Cares about

Queries the ORM issues on your behalf, and what a migration will do.

## Working rules

- Use `select_related` and `prefetch_related` deliberately; the default is N+1.
- Keep business logic out of views and templates.
- Review generated migrations, especially ones that alter or drop columns.
- Use the settings module properly; do not read environment variables at import
  time from arbitrary places.

## Pitfalls

- `QuerySet` evaluated more than once, hitting the database twice.
- Signals making control flow invisible.
- `DEBUG = True` reaching anything but a developer machine.

## Verify

Count queries in tests for anything touching the ORM. Run migrations against a
copy first.

## Defer to

`skill.safe-database-migration` for schema change.
