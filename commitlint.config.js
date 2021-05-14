const Configuration = {
    /*
   * Resolve and load @commitlint/config-conventional from node_modules.
   * Referenced packages must be installed
   */
    extends: ['@commitlint/config-conventional'],
    parserPreset: {
        parserOpts: {
            issuePrefixes: ['LT-']
        }
    },
    /*
   * Resolve and load @commitlint/format from node_modules.
   * Referenced package must be installed
   */
    formatter: '@commitlint/format',
    /*
   * Any rules defined here will override rules from @commitlint/config-conventional
   */
    rules: {
        'references-empty': [2, 'never']
    },
};

module.exports = Configuration;
