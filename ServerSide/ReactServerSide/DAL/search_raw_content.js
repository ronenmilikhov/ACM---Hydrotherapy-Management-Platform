const fs = require('fs');
const path = require('path');

function walk(dir) {
  let results = [];
  const list = fs.readdirSync(dir);
  list.forEach(file => {
    if (file === 'bin' || file === 'obj' || file === '.git' || file === 'node_modules' || file === '.expo' || file === 'node_modules') return;
    const fullPath = path.join(dir, file);
    const stat = fs.statSync(fullPath);
    if (stat && stat.isDirectory()) {
      results = results.concat(walk(fullPath));
    } else {
      results.push(fullPath);
    }
  });
  return results;
}

const root = path.join(__dirname, '..', '..', '..');
const files = walk(root);

files.forEach(f => {
  try {
    const content = fs.readFileSync(f, 'utf8');
    if (content.includes('בחציון המדדים') || content.includes('חציון המדדים')) {
      console.log(`Found in file: ${path.relative(root, f)}`);
      const lines = content.split('\n');
      lines.forEach((line, idx) => {
        if (line.includes('חציון')) {
          console.log(`  Line ${idx + 1}: ${line.trim()}`);
        }
      });
    }
  } catch {}
});
