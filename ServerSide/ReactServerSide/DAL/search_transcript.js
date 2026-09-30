const fs = require('fs');
const path = require('path');

const logPath = 'C:\\Users\\אור\\.gemini\\antigravity-ide\\brain\\31ed3e15-a7df-4f54-916b-493fe70112d0\\.system_generated\\logs\\transcript.jsonl';

if (fs.existsSync(logPath)) {
  const content = fs.readFileSync(logPath, 'utf8');
  const lines = content.split('\n');
  lines.forEach((line, idx) => {
    if (line.includes('gcloud') || line.includes('deploy') || line.includes('Docker')) {
      // Parse JSON line safely
      try {
        const obj = JSON.parse(line);
        if (obj.tool_calls) {
          obj.tool_calls.forEach(tc => {
            if (tc.name === 'run_command') {
              console.log(`L${idx+1}: Command: ${tc.arguments.CommandLine}`);
            }
          });
        } else if (obj.content && obj.content.includes('gcloud')) {
          console.log(`L${idx+1} Content: ${obj.content.substring(0, 300)}`);
        }
      } catch (e) {
        // Fallback simple search
        console.log(`L${idx+1} (Raw Match): ${line.substring(0, 200)}`);
      }
    }
  });
} else {
  console.log("Log file not found at path: " + logPath);
}
