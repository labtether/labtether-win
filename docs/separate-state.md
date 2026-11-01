# Run with separate application state

The normal launch still uses the installed user's LabTether settings and Windows
credential vault. To run the same app with separate state, pass one absolute
local directory:

```powershell
LabTetherAgent.exe --state-directory 'D:\LabTetherState\my-instance'
```

The directory owns settings, current-user DPAPI credentials, setup transactions,
child identity and tokens. It must be outside the installed state directory and
cannot use junctions or symbolic links. If it does not exist, the app creates it
so that only you, SYSTEM and Administrators can open it. The app refuses an
existing directory that another user owns or can open.
Each directory has its own activation key and exact child recovery record.
Start at Login and toast registration are unavailable for separate state; the
installed app's registrations and saved credentials are not changed. The normal
setup and Hub connection flow is used. Quit stops the owned child; durable state
stays in the chosen directory for later use. Removing that directory is a separate
user action. An older child without an ownership record is never killed just
because it runs the same executable.
