# Recorded app-server data

`thread-read.json` contains a sanitized `ThreadReadResponse` captured through the SDK from an authorized `thread/read` request to Codex CLI 0.163.0. It retains selected actual item types: image generation, image views, user text, assistant text, reasoning, and command execution. Identifiers, local paths, and text values have been replaced with fixture values.

The three PNG files are actual image bytes captured from that thread's image-generation result and image-view tool outputs. The SDK snapshot resolved both historical image-view items to the latest image path, so the fixture uses the corresponding individual image bytes from the source rollout. This preserves the original image-view outputs for converter tests without implying that the SDK restores historical files.

The image-generation result in the JSON is the `__FIXTURE_IMAGE_GENERATION_RESULT_BASE64__` token. Tests hydrate it from `images/image-generation.png`. Image-view paths are relative to this directory and should be resolved by the test before conversion.
