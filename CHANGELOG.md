# Changelog

## [0.2.0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/compare/v0.1.2...v0.2.0) (2026-03-18)


### ⚠ BREAKING CHANGES

* rename Z-prefixed generated names to ZeroAlloc-prefixed

### Features

* add ZeroAlloc.Validation.Inject source generator with AddZeroAllocValidators() ([778788e](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/778788ec93e3c7627f524978df106591491d6ec8))
* add ZeroAlloc.Validation.Inject, ZeroAlloc.Validation.Options, and AspNetCore rename ([c148dba](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/c148dba577b137688fbe59d65d3a698f8432c5a5))
* add ZeroAlloc.Validation.Options integration tests ([b80caed](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/b80caed7c22b30c3936de0b11bde0b22d0874a11))
* add ZeroAlloc.Validation.Options runtime project with ZeroAllocOptionsValidator&lt;T&gt; ([ed2cdca](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/ed2cdca1b5c480bcbbaba5e6fadd4b23b23f5c78))
* add ZeroAlloc.Validation.Options.Generator emitting ValidateWithZeroAlloc() overloads ([4f32ef7](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/4f32ef76fb1598bdcd2e2b27030264c7c07bdda9))
* rename Z-prefixed generated names to ZeroAlloc-prefixed ([026a720](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/026a7203e8f5b0639ec647f4d023fc73aee0e42a))
* wire ValidatorRegistrationEmitter into AspNetCore generator, resolve via ValidatorFor&lt;T&gt; ([09716fa](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/09716faf8b887823f5da1a720f9ad637137b4c12))


### Bug Fixes

* add PrivateAssets=all on Inject reference in AspNetCore.Generator ([73b83fd](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/73b83fd4e6fa729efb9d583e6371c1da7d40a656))
* emit services local in ValidateWithZeroAlloc body, add empty-input test ([39ac5eb](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/39ac5ebf7dc9d86eaa37414b2f183536ad7d4d38))
* rename test method to match ZeroAlloc-prefixed convention ([a9b4589](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/a9b458965ec637a39f648c55e0ee066ad5d4d0e6))
* skip null options in ZeroAllocOptionsValidator instead of passing to validator ([3642b96](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/3642b96ebd7c5f8a79d2770b3dc3b3683de5f23a))


### Documentation

* add Inject and Options design doc ([80f6642](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/80f66420535d84df7ac17d8494c96f94d6f1172a))
* add Inject and Options implementation plan ([c632c1f](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/c632c1f7d5980f3e98caf8fe196cd0562b976079))
* rename AddZeroAllocValidation to AddZeroAllocAspNetCoreValidation in design doc ([2f62333](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/2f62333e9d7f78b3438f1f08926b9b4cf9a72f59))
* update aspnetcore.md, add inject.md and options.md, update README with new packages ([1538f74](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/1538f745d5e3b1ff9988d898418507374d6ffccc))


### Tests

* add Inject integration tests and cross-package idempotency test ([006529d](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/006529d4ab81bdf0acaf9d84047da638bc53a9c2))

## [0.1.2](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/compare/v0.1.1...v0.1.2) (2026-03-17)


### Bug Fixes

* correct Build badge URL in README ([#3](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/issues/3)) ([bae459a](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/bae459a72fbc137b8bc5b5ce1398a043e5b0c9dc))

## [0.1.1](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/compare/v0.1.0...v0.1.1) (2026-03-17)


### Features

* add [CustomValidation] for cross-property multi-failure custom logic (ZV0013) ([7ebfbd5](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/7ebfbd55df538206fc72dfca2d0cb27ac115f973))
* add [DisplayName] attribute for display-name override in error messages ([bbfc0a4](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/bbfc0a43281994ceaac5b541194e76e59bef91c7))
* add [EmailAddress] and [Matches] validation attributes ([e1e4bab](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/e1e4babf5806f12b6d3799e94ec400e0da4466fe))
* add [Equal] and [NotEqual] validation attributes ([574ab2a](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/574ab2a0e609217279f6b2629777f9048e3b68aa))
* add [ExclusiveBetween] validation attribute ([cbdba7f](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/cbdba7fca321b42cf5d516aac9bf03495e4c88d3))
* add [GreaterThan], [LessThan], [InclusiveBetween] validation attributes ([779da6c](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/779da6c98a97d6a6d88568e8e27d1cadb5c6ebc0))
* add [GreaterThanOrEqualTo] and [LessThanOrEqualTo] validation attributes ([c7ec8c8](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/c7ec8c855b1fb3ebcc95311344ae1ae464f5e18b))
* add [IsEnumName] validation attribute ([bbad892](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/bbad892ebc28924b5b6f9f6c70070ef4d7e12d47))
* add [IsInEnum] validation attribute with prop-type forwarding in BuildCondition ([fcdca32](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/fcdca327d9ee7999d1962b02fe3e7464a30038f4))
* add [Length] validation attribute ([49bfc38](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/49bfc3865dc495ef9e26ecc7a404a1772f73bb09))
* add [MinLength] and [MaxLength] validation attributes ([7755001](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/77550011aa044e03c45995c6b1e914462e2260a8))
* add [Must] validation attribute with instance method predicate ([d46bff5](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/d46bff5c79ad4260ce9872c452e32956878100c4))
* add [NotNull] and [NotEmpty] validation attributes ([8adcfe6](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/8adcfe69200b340bf5b26ac73e5a6448871b065f))
* add [Null] and [Empty] validation attributes ([c4288a0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/c4288a051ef18b6a28b0aba06f63c8931ae18c13))
* add [PrecisionScale] validation attribute and DecimalValidator helper ([e07437c](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/e07437c469309fce30b39868f370f6dc2544a17a))
* add [SkipWhen] class-level skip guard for conditional validation bypass ([6bd981a](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/6bd981ada0fcf3e0ad2b949e4d3cf51262df5ba5))
* add [StopOnFirstFailure] attribute; default to continue mode for property rules ([bdd7db5](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/bdd7db54c4cd7bd32ad9517ab768503ed19fe182))
* add [Validate(StopOnFirstFailure = true)] for validator-level fail-fast cascade ([cdcb5ef](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/cdcb5ef879c9eded6a5d29feb5e059e37255d291))
* add [ValidateWith] attribute for explicit nested validator override ([6c3e8f1](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/6c3e8f168541b8e54332651210e770b2b324008d))
* add {PropertyValue} runtime placeholder to custom messages ([f12ec6e](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/f12ec6efeb0e97892a381554d5aa4f2c192784c6))
* add ASP.NET Core auto-validation integration tests ([e4368fe](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/e4368fed0ea28176db0dae8cd1af876ded77d559))
* add BehaviorDiscoverer to classify sync and async pipeline behaviors ([f388099](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/f38809937fc39ea65efefa87dea5ca73b5625ca1))
* add benchmark project scaffold ([d9f11ef](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/d9f11ef0c71474cba3911b4e24e978f9ba4a2b48))
* add collection [Validate] element detection helpers to RuleEmitter ([ff5387c](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/ff5387c2088c14b3c8d9dd0d8077970b97362bd5))
* add CollectionModelBenchmark and complete benchmark suite ([f7a3879](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/f7a38790a694914e032e74ee136ecffa487ae034))
* add ErrorCode and Severity named params to validation attributes ([51bcc2a](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/51bcc2a958b490472fc60ad6a7ca45c870ec4d8e))
* add FlatModelBenchmark (ZeroAlloc vs FluentValidation) ([c39a527](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/c39a527cdadbf25352547116906df802fed559b9))
* add gen-time message placeholder substitution ({PropertyName}, {ComparisonValue}, etc.) ([216e547](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/216e5477bad4d3d36a75c2f66d5717f16e019d37))
* add nested [Validate] detection helpers to RuleEmitter ([7d27392](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/7d27392a9550b75a06ac775e6a3d4f9aab513745))
* add NestedModelBenchmark (ZeroAlloc vs FluentValidation) ([a500659](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/a50065946c49426099ddc76459cf97a6bf8770ad))
* add ValidateAsync virtual method to ValidatorFor&lt;T&gt; ([5eea447](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/5eea447af7bc1fb113578b5f680476d6c68484ca))
* add ValidationAttribute base and [Validate] marker ([756794d](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/756794d0ad0c5b1bc9ba2f0768ab61ce9d59cfc3))
* add When/Unless conditional named params to ValidationAttribute ([0d1ea3e](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/0d1ea3e086088a4fd665a9b73b4c7a0698373866))
* add ZeroAlloc.Pipeline package references ([0118498](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/011849872d524d0e3158134178085ed7b95b2ff2))
* add ZV0011/ZV0012 diagnostics for [ValidateWith] attribute validation ([797a05e](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/797a05ec5cdbf992e26d1eb2aa831db4ec89c108))
* add ZV0015 diagnostic for duplicate pipeline behavior Order values ([d46c4ed](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/d46c4eda32dec0b13ecb3d03a969c6e098733cc5))
* add ZValidation.AspNetCore.Generator emitting action filter and DI extension method ([4fc908a](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/4fc908aa2d7f0822d965217cb474c40c8471cfbd))
* emit async ValidateAsync override when async behaviors present ([ff28710](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/ff28710ab2a791640eb9853f750489b86ac29e64))
* emit collection validation loops with bracket-indexed property names ([754e1ad](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/754e1ad625312348b1e82bd5cbfc856476e384a3))
* emit sync pipeline behavior chain in generated Validate() ([425488d](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/425488d339e58d3dce5ab9d5b700190077126f29))
* forward ZeroAlloc.Inject lifetime attributes from model to generated validator ([b63cf9f](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/b63cf9f1be7daa07832e095dcbe40bda0ea759fd))
* generator discovers [Validate] classes via incremental pipeline ([cce0e84](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/cce0e8400a7c63d6f28806962b17a72c1b7dcb26))
* generator emits validator class shell with correct namespace and base type ([f11ef74](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/f11ef748e382e28468b4afb5f149d799f0189e89))
* generator emits zero-alloc Validate() body with stackalloc buffer ([36cfdbf](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/36cfdbf658af0c40a92a8dc69f8fdd7deec42305))
* scaffold ZValidation core types ([bd1bfc0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/bd1bfc0d707c513482af09b3a99733bfb4a9f797))
* scaffold ZValidation.AspNetCore project ([2bdbbdd](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/2bdbbddd1523efc80a5090154af0b8a0f5ed2160))
* scaffold ZValidation.Generator (Roslyn incremental generator) ([e1ac063](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/e1ac063e7404f4489d2085c31b91fe053641d25f))
* scaffold ZValidation.Testing with framework-agnostic assertions ([6a4f71f](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/6a4f71f71ef282943ace42c8a7406dd62e752d50))
* switch to List&lt;ValidationFailure&gt; for models with nested [Validate] properties ([31e9675](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/31e96759b34a20895bdc4e61011c2badacc2d3bd))
* update ZValidationActionFilter to IAsyncActionFilter using ValidateAsync ([0cd2bab](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/0cd2babad93dbfe3bacd0d7c1a3c8d512c36a45f))
* use constructor injection for nested validator fields ([01080c9](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/01080c9373f545cbb3db63a646e3708a3a1ea476))
* wire BehaviorDiscoverer into incremental generator pipeline ([2a76a84](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/2a76a848589278fbce8af9a7aebed4d12faa6e42))
* wire ZValidation.AspNetCore.Generator as analyzer into ZValidation.AspNetCore ([9757efc](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/9757efce64fc46913c757d0ff0917317ee597da5))


### Bug Fixes

* address code review issues in benchmark suite ([e949f5f](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/e949f5f878da048263ac4f4c829d67bff2866cd5))
* advanced.md - resolve IsDraft naming conflict in SkipWhen example ([9cea361](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/9cea361f3c469478d9f0dfce9dcb1da3765a3086))
* align AddSource hint names with ZeroAlloc.Validation branding ([5a7098a](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/5a7098a50522d7544bb7165083551450a973d61f))
* correct nested-validation doc accuracy issues ([62637d6](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/62637d61f91f919ac3c8c69a582a818e4f4d6ec7))
* correct test name and improve assertion clarity in NestedValidationTests ([1bcd961](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/1bcd96153a64df6688935f1b802569b3a3df00f9))
* document Severity enum invariant; rename sb2; add escape test for ErrorCode ([d80866d](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/d80866d11c290861d71fdbc7b5578b182c840d5b))
* error-messages ErrorCode example - stack attributes on single property ([e8f57d8](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/e8f57d849f2b33f29062a6a28158105b9f0a46f7))
* improve nested-validation doc quality ([4c44120](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/4c4412035f3e39545f16e26876740a31543712bf))
* propagate ErrorCode and Severity through nested/collection validators ([d9fd77e](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/d9fd77ee88f3abf7088cae8c9dcb7eb07d8e8d9e))
* remove redundant OutputItemType=Analyzer from generator test reference ([8a39803](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/8a39803fc9bc0acbfb07a110dadf48c52545567c))
* revert generated API surface names to spec-required ZValidation* identifiers ([efda10d](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/efda10d1d7257de67db0d443f6c7af2349f3efab))
* revert Single back to First (HLQ005 forbids Single) ([dd649df](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/dd649df86a9c79e42a20459e5e248e7b52e33c22))
* use fully qualified validator name for cross-namespace nested types ([0d50c3a](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/0d50c3ad310ab2a6a3b6569f5a4bf8dd016350ed))
* use index-based variable names in collection loop to prevent collision ([c86f8a6](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/c86f8a6f548a2f9209036753a0eeeff532f3d435))
* use Single instead of First in nested metadata propagation tests ([45d46b1](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/45d46b1e9f83b01703c4086d31cc4ed6d5332a5f))
* wrap nested-path return sites in ValidateAsync, fix BehaviorCache equality and Analyzer paths ([dfa4e69](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/dfa4e697e7ec68cfcecb488b705fc9a1b69e5e69))


### Performance

* lazy-allocate flat-path buffer — zero alloc on valid path ([4c0151b](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/4c0151b70257093142e29b47310cec0fc375064d))
* replace List&lt;ValidationFailure&gt; with ArrayPool-backed FailureBuffer in mixed path ([6c29aad](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/6c29aad57725cced9bc014905bf0dc1b5eaf82f4))


### Refactoring

* add clarifying comment to EmitNestedPathStop property group handling ([3083c29](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/3083c2929c01ef403a099f33c4d7438108e77114))
* cache DiscoverAll in incremental provider and fix WrapReturnSites comment ([c78efe8](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/c78efe8e04dc77a221327f3016460db8ab940d5f))
* remove AppDomain hook and hardcoded NuGet paths from generator setup ([f5dc08b](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/f5dc08be4a7b98335c432fdf9f751c5d70a3a7a5))
* rename directories and files for ZeroAlloc.Validation rebrand ([0f2fae2](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/0f2fae2cfa4afe0fdb594ad93af127f89140eaa8))
* rename namespaces and update generator FQNs to ZeroAlloc.Validation ([d1ddc44](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/d1ddc441e26d4416f38bf509abf6e989df4fda77))
* rename test to accurately describe null guard emission check ([701179a](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/701179aa4767605b69e0e51b2a6af495acd3bebf))
* update solution and project file references ([fee38ca](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/fee38cae0bf0224841c45678bdcf6db652408920))
* update test namespaces and using statements ([bfa6e6c](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/bfa6e6c5626610eb87021e43c6352eb9acd18225))
* use internal visibility on EmitValidateBodyAsString ([ba22c7d](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/ba22c7dddea30783fd69b5524832bca378997cc2))


### Documentation

* add {PropertyValue} placeholder design doc ([f089207](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/f089207cc7ff5ba77fbf144c12c3fc2d87d164eb))
* add {PropertyValue} placeholder implementation plan ([395f867](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/395f867caa9be48d80f1155f0d59c8315ee70aab))
* add advanced features page ([41b169e](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/41b169eb24910655bcd672313c34932eafbad134))
* add ASP.NET Core + DI lifetime forwarding implementation plan ([cf00938](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/cf00938e389347ac82ba421ca22581b6205d2177))
* add aspnetcore integration page ([a17b963](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/a17b963732e232aea7b509cf340f26a2af1feda9))
* add attribute API and generator implementation plan ([9142d8c](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/9142d8c03187440ceff72fb7f872cc7e3878a620))
* add attribute API and source generator design ([23cb159](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/23cb159b6d85a24dc345f563cd2b27e30c46a1f5))
* add attribute reference page ([d9b4c72](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/d9b4c7278fdbc6df1d866a9d98851b1cf6a98d1d))
* add benchmark implementation plan ([da2efca](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/da2efca816bcdedf31efc574bb326569b55436fc))
* add benchmark project design doc ([a57e9b1](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/a57e9b1f4c8155ced3a1c4d32ae371cec1c25ea5))
* add benchmarks README with environment info and results ([1adac78](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/1adac784db6291391e0a8a2757567eb5a1adc1b0))
* add collection validation design ([c262041](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/c262041a4d4015432da43744b2dcc0599d0b5635))
* add collection validation implementation plan ([a0c7bdc](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/a0c7bdce646d36d6f8b625593cd9ee5552af3593))
* add collection-validation page ([ec3048e](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/ec3048eeb1c1ce4638e3f9dc65702a4471e430dd))
* add complex property validation design ([b7ab237](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/b7ab237652dc9622090f46bc2b0b949967aa955f))
* add custom-validation page ([20452b1](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/20452b1f2d6c913d08523aee50975b879be78191))
* add design doc for ASP.NET Core integration and DI lifetime forwarding ([a1b2d6b](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/a1b2d6bb02b2408c4a96884c1b6cd3b20550916a))
* add design doc for enriched failure metadata and cascade stop ([4105a69](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/4105a691d3d999d524413ece30a2de0fbd32b5f8))
* add design doc for missing built-in validators ([32741c6](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/32741c693546d55195c4112c784999ae6a730050))
* add design doc for nested object and collection validation ([ee8bcb5](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/ee8bcb54d7e3ad7e99183bc20d36fb6590496a55))
* add design doc for placeholders, [Must], and [When]/[Unless] ([0821487](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/0821487bbdcf9a8fd1aa76bed42370c679933b43))
* add display name override + validator-level cascade design doc ([5ee69c2](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/5ee69c21d2bbd73e562ac9f3609337e6755f4a15))
* add documentation design doc (README + Docusaurus) ([b4ea7e6](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/b4ea7e6636daefc45d3ccb716429fb2ac04eaf99))
* add documentation implementation plan ([38432c7](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/38432c75817fe1524dbd73d1c00f20ad857b9d78))
* add error-messages page ([9ee5365](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/9ee536517197499b3c68580180dc5e7c59d51ed5))
* add getting-started page ([7a778a0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/7a778a0392b1c055ba09fa1dd2dad6cdac2be31a))
* add implementation plan for enriched metadata and cascade stop ([ce085d3](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/ce085d305bc77743db9d60fe6264dee2b88bcc84))
* add implementation plan for missing built-in validators ([ec66dd6](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/ec66dd628ec7096669e17e5378e3534f215dd5ce))
* add implementation plan for nested/collection validation ([82de834](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/82de834da7033453f9275fd74f3301ee360f3936))
* add implementation plan for placeholders, [Must], and [When]/[Unless] ([1860866](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/18608668ff070f12e075103c432cfb5478009d36))
* add nested-validation page ([eabe195](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/eabe195f9fd4546334a380793181ef6b1ec4d2d0))
* add org-wide documentation uniformization design ([3cbc7ff](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/3cbc7ff47bae148cdb352979be096c7b39db31d1))
* add org-wide documentation uniformization implementation plan ([b806693](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/b806693d3d9d7921d7438c8153c690784a4e6873))
* add performance page ([6dbd0cd](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/6dbd0cd5893cc44ac80b5887e6c45c21f9a91517))
* add pipeline behaviors design doc ([bbd2b7f](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/bbd2b7fbb16eacd2d0c83e34ec60cf3b8d1a634f))
* add pipeline behaviors implementation plan ([2db285b](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/2db285bef145335bcb2a8e5bf4b8d9c7842ff129))
* add root README with pitch, examples, and performance table ([4fad9e5](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/4fad9e55bd571c11ab505897fcc297f1bb3d8106))
* add test gap closure implementation plan ([630a95f](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/630a95fe087c7bae528285a6825c9bd60c3533f9))
* add testing page ([8d4f768](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/8d4f7683ceaff00262d634e116bc2fcb0d76f333))
* add v1 features design for §4.3 CustomValidation, §15 SkipWhen, §20 FailureBuffer ([c861e58](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/c861e58d65bee5d993c6117c2f95ea56fc90ed21))
* add ZV0015 diagnostic to diagnostics reference ([b3f3d80](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/b3f3d80aa26b5523ee658b19f8a2fe388a4df55e))
* document buffer over-allocation intent in EmitFlatPath ([2cbcbab](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/2cbcbabcb57b300933c9a62bd64ec4d3105069c3))
* document clearArray: false safety and Grow() trigger condition in FailureBuffer ([443fc1c](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/443fc1c39f21e4b4362397d7582140b0cc468210))
* document omitted null-guard test in ValidateWithTests ([ca1b84f](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/ca1b84ff6002fc29b76414df43aa6bac390a7b58))
* fix attribute reference accuracy (messages, signatures, examples) ([6c95822](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/6c958223cce646a8518bdb7ed199141fc195b201))
* fix benchmarks README — remove non-existent attribute, fix FV baseline ratios ([623a848](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/623a8488a81adda0a0325fd72c418163f19b994b))
* fix generated class description in getting-started ([49ebc03](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/49ebc0349d176e1da6c7421e178ff7aecfd092d2))
* fix README quick-start example and documentation links ([4c9f00f](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/4c9f00f874a020f0cba371d99bad9c92fb801ecf))
* mark §4.3 CustomValidation, §15 SkipWhen, §20 FailureBuffer as done ([de4969f](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/de4969f8367f045c9d043eeb99d3a7d73bbfdfd7))
* mark §5.5 DisplayName and §7.2 validator-level cascade as done in features.md ([22d2126](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/22d2126f01865c9c8123fc3df011ae915d79098c))
* mark complex property and collection validation as complete in features.md ([0357949](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/0357949014bb11777e5c1b0a0daf27c939c41816))
* mark ErrorCode, Severity, and cascade stop as complete in features.md ([22f417d](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/22f417d982221920bb5654df0d759f25ef196e31))
* rewrite features.md with attribute-based API and accurate implementation status ([db88a9d](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/db88a9d8a57c012b41e899e7f1b439a1bb743743))
* **test:** document StopOnFirstFailure + CustomValidation interaction ([f3dadfe](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/f3dadfe696f52079765e222d2083fe5765cdfb41))
* update all documentation for ZeroAlloc.Validation rebrand ([41e147a](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/41e147a5963572614672ac3753492928d20a2f1d))
* update features.md with implementation status ([05590b9](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/05590b9ad24e3f7fb0464faec3312ff0b3525060))


### Tests

* add [ValidateWith] integration tests ([49d6cc3](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/49d6cc33447c789fe1a1268d986956eafd699f5d))
* add {PropertyValue} placeholder integration tests; mark feature complete ([8269abc](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/8269abc8c08a59030012b8bcf2430b6fda7c4159))
* add end-to-end integration tests for all new validators ([0d0b720](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/0d0b720a32dda9bc3d5fa59ef53f7add7b8ebc6c))
* add end-to-end integration tests for attribute-based validation ([d5fa4d5](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/d5fa4d54d23fb132ed77468d4bd7db84eb8e1277))
* add end-to-end integration tests for collection property validation ([f62f7ad](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/f62f7adb13a9428374f41d0e3cbb11208bd78492))
* add end-to-end integration tests for nested property validation ([ba3cad6](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/ba3cad6bc22a2acfd1475dca09d3e787bacd4891))
* add failing generator tests for {PropertyValue} placeholder ([3d8cafe](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/3d8cafe9a61a1f3b73f6567be7ed9fb6e1d56798))
* add generator tests for collection validation bracket notation and null guard ([2058939](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/205893927aa749afa33ed401097a8c33d5e07d26))
* add generator tests for nested validation dot-prefix and null guard ([0df1d7d](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/0df1d7dcef35e0eb7d1522e51656097208771e6c))
* add Matches integration tests and missing generator emission tests ([fb65b9b](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/fb65b9ba9643a77a61801fa2489bb0405a9a41d4))
* add Message coverage for all validation attributes ([8b7daeb](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/8b7daeb9b4a16e4c59f39121c10e6c3e2314c9ac))
* add missing attribute declaration and generator tests for [Equal] and [NotEqual] ([eec22b5](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/eec22b5bdac874f12f9c61ca7bb8284a9e8ede13))
* add MSTest compat test project ([4c753dd](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/4c753dd173b4e9fd0c55791607a2250e130275ed))
* add MustAttribute_UnlessDefaultsToNull symmetry test ([7e7a621](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/7e7a621340ba4562267e485a686d5c9c65562f28))
* add NUnit compat test project ([a7ecfcc](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/a7ecfcccc0d031a0634afaf6c4bea6eec60ec439))
* add standalone MinLength/MaxLength placeholder tests and clarify operator precedence ([f3c1544](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/f3c154499291ff2ffe87d43af040b3e25506a7b3))
* add xUnit test project with ValidationResult smoke tests ([7799c14](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/7799c14c4814d4009bab8aff623a2edac44b64a0))
* add ZV0013 diagnostic test for invalid [CustomValidation] method signature ([215215b](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/215215b4931aae1d802c9f4047346cf339cc2593))
* close remaining test gaps (sparse collections, enum name case, cascade+when) ([ba126c9](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/ba126c9dff61391ce8ef1c55935a108232a8e776))
* close test gaps (InclusiveBetween placeholder, Must+When); update features.md ([6640bb4](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/6640bb4bf3f73db1dd824f9fe85d71c19e148d47))
* fill gaps in nested validation coverage ([d2d28ae](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/d2d28ae82268992b2c63111810bff223ca717902))
* improve HasErrorWithMessage diagnostics and add Must default message test ([526895f](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/526895f4d7ca34a21ec85373a720cfa26dc160a3))
* verify ForModel returns behaviors sorted by Order ([6127d68](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/commit/6127d681eb0e97370eee4b35f808ab963ad2f35d))
