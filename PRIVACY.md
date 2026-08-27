# Privacy Policy for geetRPCS

Last Updated: August 15, 2026

geetRPCS ("we", "our", or "the Application") is designed with privacy as a core principle. This Privacy Policy explains what data we collect, how it is used, and your control over it.

## 1. Data Collection & Usage

### 1.1. Local Processing
**geetRPCS is a "Local First" application.**
- All processing of your active windows, mouse activity, and configuration happens entirely on your local machine.
- We do **not** upload your process list, window titles, or mouse statistics to any external server.

### 1.2. Discord Rich Presence
The Application connects to the local Discord client via Discord's RPC (Remote Procedure Call) API.
- **Shared Data:** The Application sends the name of the active supported application, its state (e.g., "Editing file.cs"), and timestamps to your local Discord client.
- **Privacy Control:** This data is only visible on your Discord profile based on your Discord privacy settings ("Activity Privacy").

### 1.3. No Telemetry
This fork does not contain an analytics or telemetry client. Discord usernames,
user IDs, process lists, window titles, activity bridge documents, and usage
statistics are not sent to a project-maintainer endpoint.

### 1.4. Mouse Energy Detector
This feature analyzes mouse movement speed and click frequency locally to determine an "Energy Level" (e.g., 'Focused', 'Relaxing').
- **Raw Data:** Mouse coordinates and individual clicks are processed in real-time RAM and **discarded immediately**. They are never saved to disk or transmitted.
- **Aggregated Data:** Only the calculated "Level" (e.g., "Focused") is displayed on Discord.

## 2. External Services

- **GitHub API:** Only an explicit update check or enabling Auto-Update contacts
  `api.github.com` to detect application and database updates. This interaction
  is subject to [GitHub's Privacy Statement](https://docs.github.com/en/site-policy/privacy-policies/github-privacy-statement).

## 3. Data Storage

All user data is stored locally in your `%LOCALAPPDATA%\geetRPCS\` directory:
- `settings.json`: Your configuration.
- `apps.json`: App definitions.
- `statistics.json`: Your personal usage stats (viewable only by you).
- `geetRPCS.log`: Local debug logs.
- `Languages\`: Translation files shipped with the app (updated with each release; no user data). Your language preference is stored in `settings.json`.

## 4. Updates to this Policy

We may update this policy as the application evolves. Significant changes will be communicated via the Application's update notes.

## 5. Contact

If you have questions about this Privacy Policy, please open an issue on our [GitHub Repository](https://github.com/reineowo/geetRPCS).
