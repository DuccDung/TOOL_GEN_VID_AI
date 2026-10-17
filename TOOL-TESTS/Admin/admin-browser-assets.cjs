// Read the actual local font binaries into isolated browser fixtures. No external HTTP.
const fs = require('node:fs');
const path = require('node:path');
const fonts = path.resolve(__dirname, '../../TOOL-SERVER/wwwroot/admin/fonts');
function withAdminFonts(css) {
  return css.replace(/url\("\/admin\/fonts\/(roboto-(?:latin|vietnamese)\.woff2)"\)/g,
    (_, name) => `url("data:font/woff2;base64,${fs.readFileSync(path.join(fonts, name)).toString('base64')}")`);
}
module.exports = { withAdminFonts };
